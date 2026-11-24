using Viteme.Domain.Enums;

namespace Viteme.Application.Interfaces;

/// <summary>Quem paga: o gateway real precisa de um cliente cadastrado para emitir a cobrança.</summary>
public sealed record Pagador(Guid UsuarioId, string Nome, string Email, string? CpfCnpj = null);

/// <summary>
/// Cobrança a criar no gateway. <see cref="ReferenciaExterna"/> é o id do pagamento e serve de chave de idempotência.
/// <see cref="Pagador"/> pode ser nulo (o stub não usa). <see cref="WalletRecebedor"/> é a carteira da subconta do organizador
/// (Fase 2): com ela, o valor vai para o organizador pelo split; sem ela, fica na conta raiz.
/// </summary>
public sealed record PedidoCobranca(
    Guid ReferenciaExterna, decimal Valor, string Descricao, DateTime ValidaAte, Pagador? Pagador = null, string? WalletRecebedor = null);

public sealed record CobrancaCriada(string IdTransacao, string UrlPagamento);

/// <summary>Pix da cobrança para pagar sem sair do site: "copia e cola" e imagem do QR Code (PNG em base64).</summary>
public sealed record PixCobranca(string CopiaECola, string ImagemBase64, DateTime? ExpiraEm);

/// <summary>Mudança de status de um pagamento, já traduzida do formato do gateway para o do Viteme.</summary>
public sealed record NotificacaoPagamento(
    string IdTransacao, Guid ReferenciaExterna, StatusPagamento Status, decimal Valor, string? Metodo);

/// <summary>
/// Contrato do gateway de pagamento. O fornecedor concreto (Asaas) é um adaptador em Infrastructure;
/// hoje existe um stub. As regras de negócio não conhecem o fornecedor.
/// </summary>
public interface IGatewayPagamento
{
    /// <summary>O gateway só cobra com o CPF/CNPJ do pagador (Asaas): sem ele a inscrição paga é recusada antes de reservar a vaga.</summary>
    bool ExigeDocumentoDoPagador { get; }

    /// <summary>Cria a cobrança. Deve ser idempotente para a mesma <see cref="PedidoCobranca.ReferenciaExterna"/>.</summary>
    Task<CobrancaCriada> CriarCobrancaAsync(PedidoCobranca pedido, CancellationToken cancellationToken = default);

    /// <summary>
    /// QR Code e "copia e cola" da cobrança, ou <c>null</c> se o gateway não os oferece agora (ex.: conta sem chave Pix).
    /// Nesse caso a tela usa o link de pagamento (<see cref="CobrancaCriada.UrlPagamento"/>).
    /// </summary>
    Task<PixCobranca?> ObterPixAsync(string idTransacao, CancellationToken cancellationToken = default);

    Task<StatusPagamento> ConsultarCobrancaAsync(string idTransacao, CancellationToken cancellationToken = default);

    Task SolicitarEstornoAsync(string idTransacao, decimal valor, CancellationToken cancellationToken = default);

    /// <summary>Confere a autenticidade do webhook (HMAC, token em cabeçalho etc., conforme o fornecedor).</summary>
    bool ValidarAssinaturaWebhook(string corpoBruto, IReadOnlyDictionary<string, string> cabecalhos);

    /// <summary>
    /// Traduz o corpo do webhook do fornecedor. Lança <see cref="FormatException"/> se for inválido. Retorna <c>null</c> para
    /// um evento válido que não muda o pagamento (ex.: cobrança criada): o webhook responde 200 sem processar, para o
    /// fornecedor não repetir nem pausar a fila de entregas.
    /// </summary>
    NotificacaoPagamento? InterpretarWebhook(string corpoBruto);
}

/// <summary>Só o stub implementa: permite simular o gateway na demonstração, assinando webhooks com o segredo dele.</summary>
public interface ISimuladorGateway
{
    bool Habilitado { get; }

    string NomeCabecalhoAssinatura { get; }

    string Assinar(string corpoBruto);

    string MontarWebhook(string idTransacao, Guid referenciaExterna, StatusPagamento status, decimal valor, string? metodo);
}
