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

    private readonly HttpClient _http;
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
        _http = new HttpClient(handler, disposeHandler: true)
        {
            BaseAddress = new Uri(_opcoes.UrlBase.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(_opcoes.TimeoutSegundos),
        };
        _http.DefaultRequestHeaders.Add("access_token", _opcoes.ChaveApi);
        _http.DefaultRequestHeaders.UserAgent.Add(ProductInfoHeaderValue.Parse(_opcoes.NomeAplicacao));
    }

    private bool Sandbox => _opcoes.UrlBase.Contains("sandbox", StringComparison.OrdinalIgnoreCase);

    public async Task<CobrancaCriada> CriarCobrancaAsync(PedidoCobranca pedido, CancellationToken cancellationToken = default)
    {
        var referencia = pedido.ReferenciaExterna.ToString();

        // Idempotência: a mesma referência externa devolve a cobrança já criada (retentativa após falha de rede).
        var existente = await BuscarAsync<ListaAsaas<PagamentoAsaas>>($"payments?externalReference={Uri.EscapeDataString(referencia)}", cancellationToken);
        if (existente?.Data.FirstOrDefault(p => !p.Deleted) is { } ja)
        {
            return new CobrancaCriada(ja.Id, ja.InvoiceUrl ?? string.Empty);
        }

        var pagador = pedido.Pagador ?? throw new InvalidOperationException("O Asaas exige os dados do pagador para criar a cobrança.");
        var cliente = await ObterOuCriarClienteAsync(pagador, cancellationToken);

        var corpo = new
        {
            customer = cliente,
            billingType = "PIX",
            value = pedido.Valor,
            dueDate = VencimentoDe(pedido.ValidaAte).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            description = pedido.Descricao.Length > 500 ? pedido.Descricao[..500] : pedido.Descricao,
            externalReference = referencia,
        };

        var criada = await EnviarAsync<PagamentoAsaas>(HttpMethod.Post, "payments", corpo, cancellationToken);
        _logger.LogInformation("[Asaas] cobrança {IdTransacao} de {Valor} criada para a referência {Referencia}.", criada.Id, pedido.Valor, referencia);
        return new CobrancaCriada(criada.Id, criada.InvoiceUrl ?? string.Empty);
    }

    public async Task<StatusPagamento> ConsultarCobrancaAsync(string idTransacao, CancellationToken cancellationToken = default)
    {
        var pagamento = await EnviarAsync<PagamentoAsaas>(HttpMethod.Get, $"payments/{Uri.EscapeDataString(idTransacao)}", null, cancellationToken);
        return pagamento.Deleted ? StatusPagamento.Expirado : MapearStatus(pagamento.Status);
    }

    public async Task SolicitarEstornoAsync(string idTransacao, decimal valor, CancellationToken cancellationToken = default)
    {
        // Idempotência (P30): se o estorno já foi feito ou está em andamento, não pede de novo.
        var atual = await EnviarAsync<PagamentoAsaas>(HttpMethod.Get, $"payments/{Uri.EscapeDataString(idTransacao)}", null, cancellationToken);
        if (atual.Status is "REFUNDED" or "REFUND_REQUESTED" or "REFUND_IN_PROGRESS")
        {
            _logger.LogInformation("[Asaas] estorno de {IdTransacao} já {Status}; nada a fazer.", idTransacao, atual.Status);
            return;
        }

        await EnviarAsync<PagamentoAsaas>(
            HttpMethod.Post, $"payments/{Uri.EscapeDataString(idTransacao)}/refund",
            new { value = valor, description = "Estorno de inscrição no Viteme" }, cancellationToken);
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

    public void Dispose() => _http.Dispose();

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

    private async Task<string> ObterOuCriarClienteAsync(Pagador pagador, CancellationToken cancellationToken)
    {
        var referencia = pagador.UsuarioId.ToString();
        var existentes = await BuscarAsync<ListaAsaas<ClienteAsaas>>($"customers?externalReference={Uri.EscapeDataString(referencia)}", cancellationToken);
        if (existentes?.Data.FirstOrDefault(c => !c.Deleted) is { } cliente)
        {
            return cliente.Id;
        }

        // Pergunta Q4 do plano: o Asaas exige CPF/CNPJ para criar o cliente e o Viteme ainda não coleta o do pagador.
        var documento = Sandbox ? _opcoes.CpfCnpjPagadorPadraoSandbox : string.Empty;
        if (string.IsNullOrWhiteSpace(documento))
        {
            throw new InvalidOperationException(
                "O pagador não tem CPF/CNPJ cadastrado e não há CPF padrão do sandbox configurado (Gateway:Asaas:CpfCnpjPagadorPadraoSandbox).");
        }

        var criado = await EnviarAsync<ClienteAsaas>(
            HttpMethod.Post, "customers",
            new { name = pagador.Nome, email = pagador.Email, cpfCnpj = documento, externalReference = referencia, notificationDisabled = true },
            cancellationToken);
        return criado.Id;
    }

    private Task<T?> BuscarAsync<T>(string rota, CancellationToken cancellationToken) where T : class =>
        EnviarAsync<T?>(HttpMethod.Get, rota, null, cancellationToken);

    private async Task<T> EnviarAsync<T>(HttpMethod metodo, string rota, object? corpo, CancellationToken cancellationToken)
    {
        using var requisicao = new HttpRequestMessage(metodo, rota);
        if (corpo is not null)
        {
            requisicao.Content = JsonContent.Create(corpo, options: Json);
        }

        using var resposta = await _http.SendAsync(requisicao, cancellationToken);
        if (!resposta.IsSuccessStatusCode)
        {
            var detalhe = await LerErroAsync(resposta, cancellationToken);
            throw new HttpRequestException($"Asaas respondeu {(int)resposta.StatusCode} em {metodo} {rota}: {detalhe}", null, resposta.StatusCode);
        }

        return (await resposta.Content.ReadFromJsonAsync<T>(Json, cancellationToken))!;
    }

    private static async Task<string> LerErroAsync(HttpResponseMessage resposta, CancellationToken cancellationToken)
    {
        try
        {
            var erro = await resposta.Content.ReadFromJsonAsync<ErroAsaas>(Json, cancellationToken);
            return erro?.Errors is { Count: > 0 } lista ? string.Join("; ", lista.Select(e => e.Description)) : "sem detalhe";
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return "resposta não é JSON";
        }
    }

    private sealed record ListaAsaas<T>(List<T> Data);

    private sealed record ClienteAsaas(string Id, bool Deleted);

    private sealed record PagamentoAsaas(
        string Id, string? Status, bool Deleted, string? InvoiceUrl, string? ExternalReference, decimal? Value, string? BillingType);

    private sealed record WebhookAsaas(string? Id, string? Event, PagamentoAsaas? Payment);

    private sealed record ErroAsaas(List<DetalheErroAsaas>? Errors);

    private sealed record DetalheErroAsaas(string? Code, string? Description);
}
