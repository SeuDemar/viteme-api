using Viteme.Domain.Enums;

namespace Viteme.Application.Interfaces;

/// <summary>Cobrança a criar no gateway. <see cref="ReferenciaExterna"/> é o id do pagamento e serve de chave de idempotência.</summary>
public sealed record PedidoCobranca(Guid ReferenciaExterna, decimal Valor, string Descricao, DateTime ValidaAte);

public sealed record CobrancaCriada(string IdTransacao, string UrlPagamento);

/// <summary>Mudança de status de um pagamento, já traduzida do formato do gateway para o do Viteme.</summary>
public sealed record NotificacaoPagamento(
    string IdTransacao, Guid ReferenciaExterna, StatusPagamento Status, decimal Valor, string? Metodo);

/// <summary>
/// Contrato do gateway de pagamento. O fornecedor concreto (Asaas) é um adaptador em Infrastructure;
/// hoje existe um stub. As regras de negócio não conhecem o fornecedor.
/// </summary>
public interface IGatewayPagamento
{
    /// <summary>Cria a cobrança. Deve ser idempotente para a mesma <see cref="PedidoCobranca.ReferenciaExterna"/>.</summary>
    Task<CobrancaCriada> CriarCobrancaAsync(PedidoCobranca pedido, CancellationToken cancellationToken = default);

    Task<StatusPagamento> ConsultarCobrancaAsync(string idTransacao, CancellationToken cancellationToken = default);

    Task SolicitarEstornoAsync(string idTransacao, decimal valor, CancellationToken cancellationToken = default);

    /// <summary>Confere a autenticidade do webhook (HMAC, token em cabeçalho etc., conforme o fornecedor).</summary>
    bool ValidarAssinaturaWebhook(string corpoBruto, IReadOnlyDictionary<string, string> cabecalhos);

    /// <summary>Traduz o corpo do webhook do fornecedor. Lança <see cref="FormatException"/> se for inválido.</summary>
    NotificacaoPagamento InterpretarWebhook(string corpoBruto);
}

/// <summary>Só o stub implementa: permite simular o gateway na demonstração, assinando webhooks com o segredo dele.</summary>
public interface ISimuladorGateway
{
    bool Habilitado { get; }

    string NomeCabecalhoAssinatura { get; }

    string Assinar(string corpoBruto);

    string MontarWebhook(string idTransacao, Guid referenciaExterna, StatusPagamento status, decimal valor, string? metodo);
}
