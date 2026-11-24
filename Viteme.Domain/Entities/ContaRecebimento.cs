using Viteme.Domain.Enums;
using Viteme.Domain.Excecoes;

namespace Viteme.Domain.Entities;

/// <summary>
/// Subconta do organizador no gateway (Fase 2 do Asaas): recebe o split das cobranças dos eventos pagos.
/// Uma por usuário. A chave de API da subconta só aparece na criação e é guardada cifrada (AES-GCM, chave mestra fora do banco).
/// </summary>
public class ContaRecebimento
{
    private ContaRecebimento()
    {
    }

    public ContaRecebimento(Guid usuarioId, string contaGatewayId, string walletId, string chaveApiCifrada, string? urlOnboarding)
    {
        if (string.IsNullOrWhiteSpace(contaGatewayId) || string.IsNullOrWhiteSpace(walletId) || string.IsNullOrWhiteSpace(chaveApiCifrada))
        {
            throw new RegraDeNegocioException("CONTA_RECEBIMENTO_INVALIDA", "Conta de recebimento inválida",
                "O gateway não devolveu a conta, a carteira ou a chave da subconta.", 502);
        }

        Id = Guid.NewGuid();
        UsuarioId = usuarioId;
        ContaGatewayId = contaGatewayId;
        WalletId = walletId;
        ChaveApiCifrada = chaveApiCifrada;
        UrlOnboarding = urlOnboarding;
        Situacao = SituacaoContaRecebimento.Pendente;
    }

    public Guid Id { get; private set; }

    public Guid UsuarioId { get; private set; }

    /// <summary>Id da subconta no gateway (<c>id</c> de <c>POST /v3/accounts</c>).</summary>
    public string ContaGatewayId { get; private set; } = null!;

    /// <summary>Carteira que recebe o split (<c>walletId</c>).</summary>
    public string WalletId { get; private set; } = null!;

    /// <summary>Chave de API da subconta, cifrada. Necessária para consultar e liberar a garantia (Fase 3).</summary>
    public string ChaveApiCifrada { get; private set; } = null!;

    public SituacaoContaRecebimento Situacao { get; private set; }

    /// <summary>Página do gateway onde o organizador envia os documentos da análise cadastral.</summary>
    public string? UrlOnboarding { get; private set; }

    public DateTime? AprovadaEm { get; private set; }

    /// <summary>Quando a Conta Escrow (garantia, Fase 3) foi ligada no gateway. Só recebimentos posteriores ficam retidos.</summary>
    public DateTime? GarantiaHabilitadaEm { get; private set; }

    public DateTime CriadoEm { get; private set; }

    public DateTime AtualizadoEm { get; private set; }

    public bool EstaAprovada => Situacao == SituacaoContaRecebimento.Aprovada;

    /// <summary>
    /// Atualiza a situação vinda do gateway (webhook ou consulta periódica). O gateway é a fonte da verdade: uma conta
    /// rejeitada pode ser aprovada depois do reenvio dos documentos. <see cref="AprovadaEm"/> guarda a primeira aprovação.
    /// </summary>
    public void AtualizarSituacao(SituacaoContaRecebimento nova, DateTime agora)
    {
        Situacao = nova;
        if (nova == SituacaoContaRecebimento.Aprovada && AprovadaEm is null)
        {
            AprovadaEm = agora;
        }
    }

    public void AtualizarUrlOnboarding(string? url) => UrlOnboarding = url;

    /// <summary>Registra que a garantia foi ligada no gateway. Só faz sentido com a conta aprovada; repetir não muda a data.</summary>
    public void RegistrarGarantiaHabilitada(DateTime agora)
    {
        if (!EstaAprovada)
        {
            throw new RegraDeNegocioException("CONTA_RECEBIMENTO_PENDENTE", "Conta de recebimento pendente",
                "A garantia só é ligada depois que a conta de recebimento é aprovada.");
        }

        GarantiaHabilitadaEm ??= agora;
    }
}
