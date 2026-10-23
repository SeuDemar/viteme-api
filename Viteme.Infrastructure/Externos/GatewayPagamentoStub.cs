using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Viteme.Application.Interfaces;
using Viteme.Domain.Enums;

namespace Viteme.Infrastructure.Externos;

/// <summary>Seção <c>Gateway:Stub</c> da configuração.</summary>
public class GatewayStubOptions
{
    public const string Secao = "Gateway:Stub";

    /// <summary>Segredo para assinar/validar webhooks. Vazio = gera um segredo efêmero (só o simulador interno consegue assinar).</summary>
    public string SegredoWebhook { get; set; } = string.Empty;

    /// <summary>Liga o simulador de pagamento (<c>/api/v1/simulador/...</c>) para demonstrações. Nunca em produção.</summary>
    public bool HabilitarSimulador { get; set; }

    /// <summary>Base da URL de pagamento fictícia devolvida pelo stub.</summary>
    public string UrlBase { get; set; } = "https://pagamento.stub.viteme.local/cobranca";
}

/// <summary>
/// Gateway de pagamento fictício: não cobra nem estorna de verdade. Cria cobranças idempotentes e valida webhooks
/// com HMAC-SHA256 no cabeçalho <c>X-Viteme-Assinatura</c>. Será trocado pelo adaptador do Asaas, sem mudar as regras.
/// </summary>
public class GatewayPagamentoStub(IOptions<GatewayStubOptions> opcoes, ILogger<GatewayPagamentoStub> logger)
    : IGatewayPagamento, ISimuladorGateway
{
    public const string CabecalhoAssinatura = "X-Viteme-Assinatura";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly byte[] _segredo = string.IsNullOrWhiteSpace(opcoes.Value.SegredoWebhook)
        ? RandomNumberGenerator.GetBytes(32)
        : Encoding.UTF8.GetBytes(opcoes.Value.SegredoWebhook);

    public bool Habilitado => opcoes.Value.HabilitarSimulador;

    public string NomeCabecalhoAssinatura => CabecalhoAssinatura;

    public virtual Task<CobrancaCriada> CriarCobrancaAsync(PedidoCobranca pedido, CancellationToken cancellationToken = default)
    {
        // Idempotente: a mesma referência externa gera sempre o mesmo id de transação.
        var id = $"stub_{pedido.ReferenciaExterna:N}";
        logger.LogInformation("[Gateway stub] cobrança {IdTransacao} de {Valor} criada.", id, pedido.Valor);
        return Task.FromResult(new CobrancaCriada(id, $"{opcoes.Value.UrlBase.TrimEnd('/')}/{id}"));
    }

    public virtual Task<StatusPagamento> ConsultarCobrancaAsync(string idTransacao, CancellationToken cancellationToken = default) =>
        Task.FromResult(StatusPagamento.Pendente);

    public virtual Task SolicitarEstornoAsync(string idTransacao, decimal valor, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("[Gateway stub] estorno de {Valor} solicitado para {IdTransacao}.", valor, idTransacao);
        return Task.CompletedTask;
    }

    public bool ValidarAssinaturaWebhook(string corpoBruto, IReadOnlyDictionary<string, string> cabecalhos)
    {
        if (!cabecalhos.TryGetValue(CabecalhoAssinatura, out var informada) || string.IsNullOrWhiteSpace(informada))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(Assinar(corpoBruto)),
            Encoding.ASCII.GetBytes(informada.Trim().ToUpperInvariant()));
    }

    public string Assinar(string corpoBruto) => Convert.ToHexString(HMACSHA256.HashData(_segredo, Encoding.UTF8.GetBytes(corpoBruto)));

    public NotificacaoPagamento InterpretarWebhook(string corpoBruto)
    {
        try
        {
            var dados = JsonSerializer.Deserialize<CorpoWebhook>(corpoBruto, Json)
                ?? throw new FormatException("O corpo do webhook está vazio.");

            if (string.IsNullOrWhiteSpace(dados.IdTransacao) || dados.ReferenciaExterna == Guid.Empty
                || string.IsNullOrWhiteSpace(dados.Status) || dados.Valor is null)
            {
                throw new FormatException("O webhook deve informar idTransacao, referenciaExterna, status e valor.");
            }

            if (!Enum.TryParse<StatusPagamento>(dados.Status.Replace("_", string.Empty), ignoreCase: true, out var status))
            {
                throw new FormatException($"Status de pagamento desconhecido: {dados.Status}.");
            }

            return new NotificacaoPagamento(dados.IdTransacao, dados.ReferenciaExterna, status, dados.Valor.Value, dados.Metodo);
        }
        catch (JsonException ex)
        {
            throw new FormatException("O corpo do webhook não é um JSON válido.", ex);
        }
    }

    public string MontarWebhook(string idTransacao, Guid referenciaExterna, StatusPagamento status, decimal valor, string? metodo) =>
        JsonSerializer.Serialize(new CorpoWebhook(idTransacao, referenciaExterna, status.ToString().ToUpperInvariant(), valor, metodo), Json);

    private sealed record CorpoWebhook(string? IdTransacao, Guid ReferenciaExterna, string? Status, decimal? Valor, string? Metodo);
}
