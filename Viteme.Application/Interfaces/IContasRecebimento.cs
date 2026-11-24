using Viteme.Domain.Enums;

namespace Viteme.Application.Interfaces;

/// <summary>
/// Dados da empresa do organizador para abrir a subconta no gateway. CNPJ e razão social vêm do perfil verificado (RN24);
/// o resto o organizador informa no formulário de E41.
/// </summary>
public sealed record DadosSubconta(
    Guid UsuarioId,
    string Cnpj,
    string RazaoSocial,
    string Email,
    string TipoEmpresa,
    string Telefone,
    decimal FaturamentoMensal,
    string Cep,
    string Endereco,
    string Numero,
    string? Complemento,
    string Bairro);

/// <summary>Subconta recém-criada. <see cref="ChaveApi"/> só é devolvida nesta resposta: quem chama precisa guardá-la (cifrada).</summary>
public sealed record SubcontaCriada(string ContaId, string WalletId, string ChaveApi, string? UrlOnboarding);

/// <summary>Situação da análise cadastral da subconta e a página onde o organizador envia os documentos.</summary>
public sealed record SituacaoSubconta(SituacaoContaRecebimento Situacao, string? UrlOnboarding);

/// <summary>Mudança de situação de uma subconta informada por webhook.</summary>
public sealed record NotificacaoConta(string ContaId, SituacaoContaRecebimento Situacao);

/// <summary>
/// Subconta do organizador e garantia (Fases 2 e 3 do plano de pagamentos). Separado de <see cref="IGatewayPagamento"/> para
/// não misturar cobrança com cadastro. As duas fases são ligadas pela configuração; desligadas, nada aqui é chamado.
/// </summary>
public interface IContasRecebimento
{
    /// <summary>Fase 2 ligada: conta de recebimento (E41/E42), split na cobrança e RN25.</summary>
    bool SubcontasHabilitadas { get; }

    /// <summary>Fase 3 ligada: o valor do split fica retido até a liberação (RN26).</summary>
    bool GarantiaHabilitada { get; }

    Task<SubcontaCriada> CriarSubcontaAsync(DadosSubconta dados, CancellationToken cancellationToken = default);

    /// <summary>Consulta a análise cadastral com a chave da própria subconta (já decifrada).</summary>
    Task<SituacaoSubconta> ConsultarSituacaoAsync(string chaveApiSubconta, CancellationToken cancellationToken = default);

    /// <summary>
    /// Traduz o webhook de situação de conta. Retorna <c>null</c> se o corpo não é um evento de conta (ex.: é de cobrança).
    /// Lança <see cref="FormatException"/> se for um evento de conta malformado.
    /// </summary>
    NotificacaoConta? InterpretarWebhookConta(string corpoBruto);

    /// <summary>Liga a Conta Escrow na subconta. Só os recebimentos depois disso ficam retidos.</summary>
    Task HabilitarGarantiaAsync(string contaId, CancellationToken cancellationToken = default);

    /// <summary>Id da garantia de uma cobrança aprovada, ou <c>null</c> se ela não ficou retida.</summary>
    Task<string?> ObterGarantiaAsync(string idTransacao, string chaveApiSubconta, CancellationToken cancellationToken = default);

    /// <summary>
    /// Encerra a garantia da cobrança <paramref name="idTransacao"/> e libera o valor para a subconta (RN26).
    /// Repetir para uma garantia já encerrada não falha.
    /// </summary>
    Task EncerrarGarantiaAsync(string idTransacao, string garantiaId, string chaveApiSubconta, CancellationToken cancellationToken = default);
}
