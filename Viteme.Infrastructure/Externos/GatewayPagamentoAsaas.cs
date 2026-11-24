using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Viteme.Application.Interfaces;
using Viteme.Domain.Enums;

namespace Viteme.Infrastructure.Externos;

/// <summary>Seção <c>Gateway:Asaas</c> da configuração. A chave e o token do webhook só entram por variável de ambiente.</summary>
public class GatewayAsaasOptions
{
    public const string Secao = "Gateway:Asaas";

    /// <summary>Sandbox por padrão. Produção: <c>https://api.asaas.com/v3</c>.</summary>
    public string UrlBase { get; set; } = "https://api-sandbox.asaas.com/v3";

    /// <summary>Chave de API (cabeçalho <c>access_token</c>). Nunca no repositório.</summary>
    public string ChaveApi { get; set; } = string.Empty;

    /// <summary>Token que o Asaas envia no cabeçalho <c>asaas-access-token</c> de cada webhook. Não reutilizar a chave de API.</summary>
    public string TokenWebhook { get; set; } = string.Empty;

    public int TimeoutSegundos { get; set; } = 15;

    /// <summary>Identificação enviada no <c>User-Agent</c>.</summary>
    public string NomeAplicacao { get; set; } = "Viteme/1.0";

    /// <summary>
    /// CPF de teste usado para criar o cliente quando o usuário não tem documento (pendência P1 / pergunta Q4 do plano).
    /// Só vale no sandbox: com a URL de produção é ignorado e a cobrança falha.
    /// </summary>
    public string CpfCnpjPagadorPadraoSandbox { get; set; } = string.Empty;

    /// <summary>
    /// Chave mestra (32 bytes em base64) que cifra a chave de API de cada subconta (AES-GCM). Só por variável de ambiente.
    /// Obrigatória quando <see cref="Subcontas"/> está ligada.
    /// </summary>
    public string ChaveCifragemSubcontas { get; set; } = string.Empty;

    /// <summary>Fase 2 (plano pagamentos-asaas.md, seção 8): subconta do organizador e split. Desligada por padrão.</summary>
    public SubcontasAsaasOptions Subcontas { get; set; } = new();

    /// <summary>Fase 3: Conta Escrow (garantia) nas subcontas. Desligada por padrão; exige <see cref="Subcontas"/> ligada.</summary>
    public EscrowAsaasOptions Escrow { get; set; } = new();

    /// <summary>
    /// Confere a combinação das chaves. Devolve as mensagens de erro (vazio quando está tudo certo); a inicialização da API
    /// falha com elas, para não ligar subconta ou garantia pela metade.
    /// </summary>
    public IReadOnlyList<string> Validar()
    {
        var erros = new List<string>();
        if (Escrow.Habilitar && !Subcontas.Habilitar)
        {
            erros.Add("Gateway:Asaas:Escrow:Habilitar exige Gateway:Asaas:Subcontas:Habilitar (a garantia é da subconta).");
        }

        if (Subcontas.Habilitar && !ChaveCifragemValida(ChaveCifragemSubcontas))
        {
            erros.Add("Gateway:Asaas:Subcontas:Habilitar exige Gateway:Asaas:ChaveCifragemSubcontas com 32 bytes em base64 (openssl rand -base64 32).");
        }

        if (Escrow.Habilitar && Escrow.DiasParaExpirar is < 1 or > 365)
        {
            erros.Add("Gateway:Asaas:Escrow:DiasParaExpirar deve ficar entre 1 e 365.");
        }

        return erros;
    }

    private static bool ChaveCifragemValida(string chave)
    {
        if (string.IsNullOrWhiteSpace(chave))
        {
            return false;
        }

        var bytes = new byte[64];
        return Convert.TryFromBase64String(chave.Trim(), bytes, out var tamanho) && tamanho == 32;
    }
}

public class SubcontasAsaasOptions
{
    /// <summary><c>true</c> liga E41/E42 (conta de recebimento), o split na cobrança e a RN25.</summary>
    public bool Habilitar { get; set; }

    /// <summary>
    /// URL do webhook da API (<c>https://&lt;api&gt;/api/v1/pagamentos/webhook</c>) cadastrada em cada subconta nova, para a
    /// situação cadastral chegar na hora. Vazia: só a tarefa periódica atualiza a situação.
    /// </summary>
    public string UrlWebhook { get; set; } = string.Empty;
}

public class EscrowAsaasOptions
{
    /// <summary><c>true</c> liga a garantia: o valor fica retido até a liberação depois do evento (RN26).</summary>
    public bool Habilitar { get; set; }

    /// <summary>Dias até o Asaas expirar a garantia sozinho, caso a tarefa de liberação falhe.</summary>
    public int DiasParaExpirar { get; set; } = 60;

    /// <summary>
    /// <c>true</c>: a mensalidade do escrow é cobrada da subconta (<c>isFeePayer</c>); <c>false</c>: da conta raiz.
    /// Decisão da equipe antes de produção.
    /// </summary>
    public bool SubcontaPagaMensalidade { get; set; }
}

/// <summary>
/// Adaptador do Asaas (Pix). Cria o cliente do pagador (uma vez por usuário), a cobrança Pix e devolve o link da fatura
/// (<c>invoiceUrl</c>, com o QR Code). A criação é idempotente pela <c>externalReference</c> (id do pagamento) e o estorno
/// consulta a cobrança antes de pedir de novo (P30). O webhook é autenticado pelo cabeçalho <c>asaas-access-token</c>.
/// Resposta fora de 2xx sobe como <see cref="HttpRequestException"/> (o serviço traduz para 502).
/// </summary>
public sealed class GatewayPagamentoAsaas : IGatewayPagamento, IDisposable
{
    public const string CabecalhoToken = "asaas-access-token";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ApiAsaas _api;
    private readonly GatewayAsaasOptions _opcoes;
    private readonly ILogger<GatewayPagamentoAsaas> _logger;

    public GatewayPagamentoAsaas(IOptions<GatewayAsaasOptions> opcoes, ILogger<GatewayPagamentoAsaas> logger)
        : this(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) }, opcoes, logger)
    {
    }

    public GatewayPagamentoAsaas(HttpMessageHandler handler, IOptions<GatewayAsaasOptions> opcoes, ILogger<GatewayPagamentoAsaas> logger)
    {
        _opcoes = opcoes.Value;
        _logger = logger;
        _api = new ApiAsaas(handler, _opcoes);
    }

    public bool ExigeDocumentoDoPagador => true;

    private bool Sandbox => _opcoes.UrlBase.Contains("sandbox", StringComparison.OrdinalIgnoreCase);

    public async Task<CobrancaCriada> CriarCobrancaAsync(PedidoCobranca pedido, CancellationToken cancellationToken = default)
    {
        var referencia = pedido.ReferenciaExterna.ToString();

        // Fase 3: com a chave da subconta, cliente e cobrança nascem nela (única forma de o valor ficar em garantia).
        var chave = pedido.ChaveApiRecebedor;

        // Idempotência: a mesma referência externa devolve a cobrança já criada (retentativa após falha de rede).
        var existente = await BuscarAsync<ListaAsaas<PagamentoAsaas>>($"payments?externalReference={Uri.EscapeDataString(referencia)}", chave, cancellationToken);
        if (existente?.Data.FirstOrDefault(p => !p.Deleted) is { } ja)
        {
            return new CobrancaCriada(ja.Id, ja.InvoiceUrl ?? string.Empty);
        }

        var pagador = pedido.Pagador ?? throw new InvalidOperationException("O Asaas exige os dados do pagador para criar a cobrança.");
        var cliente = await ObterOuCriarClienteAsync(pagador, chave, cancellationToken);

        var corpo = new Dictionary<string, object>
        {
            ["customer"] = cliente,
            ["billingType"] = "PIX",
            ["value"] = pedido.Valor,
            ["dueDate"] = VencimentoDe(pedido.ValidaAte).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["description"] = pedido.Descricao.Length > 500 ? pedido.Descricao[..500] : pedido.Descricao,
            ["externalReference"] = referencia,
        };

        // Fase 2: o valor líquido vai inteiro para a subconta do organizador. Com a Conta Escrow ligada nela (Fase 3), fica
        // retido até a liberação (RN26). Taxa da plataforma sobre o split: decisão em aberto (hoje 100% ao organizador).
        if (chave is null && !string.IsNullOrWhiteSpace(pedido.WalletRecebedor))
        {
            corpo["split"] = new[] { new { walletId = pedido.WalletRecebedor, percentualValue = 100 } };
        }

        var criada = await EnviarAsync<PagamentoAsaas>(HttpMethod.Post, "payments", corpo, chave, cancellationToken);
        _logger.LogInformation("[Asaas] cobrança {IdTransacao} de {Valor} criada para a referência {Referencia}.", criada.Id, pedido.Valor, referencia);
        return new CobrancaCriada(criada.Id, criada.InvoiceUrl ?? string.Empty);
    }

    public async Task<PixCobranca?> ObterPixAsync(string idTransacao, string? chaveRecebedor = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var qr = await EnviarAsync<PixAsaas>(HttpMethod.Get, $"payments/{Uri.EscapeDataString(idTransacao)}/pixQrCode", null, chaveRecebedor, cancellationToken);
            if (string.IsNullOrWhiteSpace(qr.Payload) || string.IsNullOrWhiteSpace(qr.EncodedImage))
            {
                return null;
            }

            return new PixCobranca(qr.Payload, qr.EncodedImage, DateTime.TryParse(qr.ExpirationDate, out var expira) ? expira : null);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            // Conta sem chave Pix cadastrada (ou cobrança já paga/cancelada): a tela cai no link da fatura.
            _logger.LogWarning("[Asaas] QR Code Pix indisponível para {IdTransacao}: {Motivo}", idTransacao, ex.Message);
            return null;
        }
    }

    public async Task<StatusPagamento> ConsultarCobrancaAsync(string idTransacao, string? chaveRecebedor = null, CancellationToken cancellationToken = default)
    {
        var pagamento = await EnviarAsync<PagamentoAsaas>(HttpMethod.Get, $"payments/{Uri.EscapeDataString(idTransacao)}", null, chaveRecebedor, cancellationToken);
        return pagamento.Deleted ? StatusPagamento.Expirado : MapearStatus(pagamento.Status);
    }

    public async Task SolicitarEstornoAsync(string idTransacao, decimal valor, string? chaveRecebedor = null, CancellationToken cancellationToken = default)
    {
        // Idempotência (P30): se o estorno já foi feito ou está em andamento, não pede de novo.
        var atual = await EnviarAsync<PagamentoAsaas>(HttpMethod.Get, $"payments/{Uri.EscapeDataString(idTransacao)}", null, chaveRecebedor, cancellationToken);
        if (atual.Status is "REFUNDED" or "REFUND_REQUESTED" or "REFUND_IN_PROGRESS")
        {
            _logger.LogInformation("[Asaas] estorno de {IdTransacao} já {Status}; nada a fazer.", idTransacao, atual.Status);
            return;
        }

        await EnviarAsync<PagamentoAsaas>(
            HttpMethod.Post, $"payments/{Uri.EscapeDataString(idTransacao)}/refund",
            new { value = valor, description = "Estorno de inscrição no Viteme" }, chaveRecebedor, cancellationToken);
        _logger.LogInformation("[Asaas] estorno de {Valor} solicitado para {IdTransacao}.", valor, idTransacao);
    }

    public bool ValidarAssinaturaWebhook(string corpoBruto, IReadOnlyDictionary<string, string> cabecalhos)
    {
        // Sem token configurado nenhum webhook é aceito: melhor recusar tudo do que ficar aberto.
        if (string.IsNullOrWhiteSpace(_opcoes.TokenWebhook)
            || !cabecalhos.TryGetValue(CabecalhoToken, out var informado) || string.IsNullOrEmpty(informado))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(informado.Trim()), Encoding.UTF8.GetBytes(_opcoes.TokenWebhook));
    }

    public NotificacaoPagamento? InterpretarWebhook(string corpoBruto)
    {
        WebhookAsaas dados;
        try
        {
            dados = JsonSerializer.Deserialize<WebhookAsaas>(corpoBruto, Json) ?? throw new FormatException("O corpo do webhook está vazio.");
        }
        catch (JsonException ex)
        {
            throw new FormatException("O corpo do webhook não é um JSON válido.", ex);
        }

        if (string.IsNullOrWhiteSpace(dados.Event))
        {
            throw new FormatException("O webhook deve informar o evento.");
        }

        // Eventos de outras entidades (conta, transferência...) e eventos de cobrança sem efeito no Viteme são ignorados.
        var status = MapearEvento(dados.Event);
        if (status is null || dados.Payment is null)
        {
            return null;
        }

        var pagamento = dados.Payment;
        if (string.IsNullOrWhiteSpace(pagamento.Id) || !Guid.TryParse(pagamento.ExternalReference, out var referencia) || pagamento.Value is null)
        {
            throw new FormatException("O webhook deve informar payment.id, payment.externalReference e payment.value.");
        }

        return new NotificacaoPagamento(pagamento.Id, referencia, status.Value, pagamento.Value.Value, pagamento.BillingType);
    }

    public void Dispose() => _api.Dispose();

    // Pix pago ou confirmado aprova; vencida ou apagada expira; estorno total estorna. O resto não muda o pagamento.
    private static StatusPagamento? MapearEvento(string evento) => evento switch
    {
        "PAYMENT_RECEIVED" or "PAYMENT_CONFIRMED" => StatusPagamento.Aprovado,
        "PAYMENT_OVERDUE" or "PAYMENT_DELETED" => StatusPagamento.Expirado,
        "PAYMENT_REFUNDED" => StatusPagamento.Estornado,
        _ => null,
    };

    private static StatusPagamento MapearStatus(string? status) => status switch
    {
        "RECEIVED" or "CONFIRMED" or "RECEIVED_IN_CASH" => StatusPagamento.Aprovado,
        "OVERDUE" => StatusPagamento.Expirado,
        "REFUNDED" => StatusPagamento.Estornado,
        _ => StatusPagamento.Pendente,
    };

    // O Asaas só aceita vencimento por data e não aceita data passada: usa o dia (horário de Brasília) do fim da reserva.
    private static DateOnly VencimentoDe(DateTime validaAteUtc)
    {
        var brasilia = TimeZoneInfo.FindSystemTimeZoneById(OperatingSystem.IsWindows() ? "E. South America Standard Time" : "America/Sao_Paulo");
        var hoje = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, brasilia));
        var data = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(validaAteUtc, DateTimeKind.Utc), brasilia));
        return data < hoje ? hoje : data;
    }

    private async Task<string> ObterOuCriarClienteAsync(Pagador pagador, string? chave, CancellationToken cancellationToken)
    {
        var referencia = pagador.UsuarioId.ToString();
        var existentes = await BuscarAsync<ListaAsaas<ClienteAsaas>>($"customers?externalReference={Uri.EscapeDataString(referencia)}", chave, cancellationToken);
        if (existentes?.Data.FirstOrDefault(c => !c.Deleted) is { } cliente)
        {
            // Cliente criado antes de o usuário informar o CPF (ou com outro CPF): atualiza para a cobrança ser aceita.
            if (!string.IsNullOrWhiteSpace(pagador.CpfCnpj) && cliente.CpfCnpj != pagador.CpfCnpj)
            {
                await EnviarAsync<ClienteAsaas>(HttpMethod.Put, $"customers/{cliente.Id}", new { cpfCnpj = pagador.CpfCnpj }, chave, cancellationToken);
            }

            return cliente.Id;
        }

        // Pergunta Q4 do plano: o Asaas exige CPF/CNPJ para criar o cliente e o Viteme ainda não coleta o do pagador.
        var documento = !string.IsNullOrWhiteSpace(pagador.CpfCnpj)
            ? pagador.CpfCnpj
            : Sandbox ? _opcoes.CpfCnpjPagadorPadraoSandbox : string.Empty;
        if (string.IsNullOrWhiteSpace(documento))
        {
            throw new InvalidOperationException(
                "O pagador não tem CPF/CNPJ cadastrado e não há CPF padrão do sandbox configurado (Gateway:Asaas:CpfCnpjPagadorPadraoSandbox).");
        }

        var criado = await EnviarAsync<ClienteAsaas>(
            HttpMethod.Post, "customers",
            new { name = pagador.Nome, email = pagador.Email, cpfCnpj = documento, externalReference = referencia, notificationDisabled = true },
            chave, cancellationToken);
        return criado.Id;
    }

    private Task<T?> BuscarAsync<T>(string rota, string? chave, CancellationToken cancellationToken) where T : class =>
        _api.EnviarAsync<T?>(HttpMethod.Get, rota, null, chave, cancellationToken);

    /// <summary>Chave nula: conta raiz (configuração). Com a chave da subconta, a chamada é feita como ela (Fase 3).</summary>
    private Task<T> EnviarAsync<T>(HttpMethod metodo, string rota, object? corpo, string? chave, CancellationToken cancellationToken) =>
        _api.EnviarAsync<T>(metodo, rota, corpo, chave, cancellationToken);

    private sealed record ListaAsaas<T>(List<T> Data);

    private sealed record ClienteAsaas(string Id, bool Deleted, string? CpfCnpj = null);

    private sealed record PagamentoAsaas(
        string Id, string? Status, bool Deleted, string? InvoiceUrl, string? ExternalReference, decimal? Value, string? BillingType);

    private sealed record PixAsaas(string? EncodedImage, string? Payload, string? ExpirationDate);

    private sealed record WebhookAsaas(string? Id, string? Event, PagamentoAsaas? Payment);

}
