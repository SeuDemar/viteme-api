using Viteme.Domain.Enums;
using Viteme.Domain.Excecoes;

namespace Viteme.Domain.Entities;

/// <summary>Inscrição de um participante em um evento.</summary>
public class Inscricao
{
    public static readonly TimeSpan DuracaoReserva = TimeSpan.FromMinutes(30);

    public static readonly TimeSpan AntecedenciaCancelamento = TimeSpan.FromHours(24);

    public static readonly TimeSpan AntecedenciaCheckin = TimeSpan.FromHours(2);

    private Inscricao()
    {
    }

    private Inscricao(Guid eventoId, Guid usuarioId, string codigoCheckin, StatusInscricao status, DateTime? reservadaAte)
    {
        Id = Guid.NewGuid();
        EventoId = eventoId;
        UsuarioId = usuarioId;
        CodigoCheckin = codigoCheckin;
        Status = status;
        ReservadaAte = reservadaAte;
    }

    public Guid Id { get; private set; }

    public Guid EventoId { get; private set; }

    public Guid UsuarioId { get; private set; }

    public StatusInscricao Status { get; private set; }

    public string CodigoCheckin { get; private set; } = null!;

    public DateTime? ReservadaAte { get; private set; }

    public bool PresencaConfirmada { get; private set; }

    public DateTime? CheckinEm { get; private set; }

    public DateTime CriadoEm { get; private set; }

    public DateTime AtualizadoEm { get; private set; }

    /// <summary>Coluna de sistema <c>xmin</c>: impede que webhook, expiração e cancelamento sobrescrevam um ao outro.</summary>
    public uint Versao { get; private set; }

    public ICollection<Pagamento> Pagamentos { get; } = new List<Pagamento>();

    /// <summary>RN10: em evento gratuito a inscrição é confirmada na solicitação.</summary>
    public static Inscricao CriarGratuita(Guid eventoId, Guid usuarioId, string codigoCheckin) =>
        new(eventoId, usuarioId, codigoCheckin, StatusInscricao.Confirmada, null);

    /// <summary>RN11: evento pago reserva a vaga por 30 minutos até o pagamento ser aprovado.</summary>
    public static Inscricao CriarPendentePagamento(Guid eventoId, Guid usuarioId, string codigoCheckin, DateTime agora) =>
        new(eventoId, usuarioId, codigoCheckin, StatusInscricao.PendentePagamento, agora + DuracaoReserva);

    public bool Ativa => Status is StatusInscricao.PendentePagamento or StatusInscricao.Confirmada;

    public bool ReservaVencida(DateTime agora) =>
        Status == StatusInscricao.PendentePagamento && ReservadaAte is { } ate && ate <= agora;

    public void Confirmar()
    {
        if (Status != StatusInscricao.PendentePagamento)
        {
            throw new RegraDeNegocioException("INSCRICAO_NAO_PENDENTE", "Inscrição não pendente",
                "Somente uma inscrição pendente de pagamento pode ser confirmada.", 409);
        }

        Status = StatusInscricao.Confirmada;
        ReservadaAte = null;
    }

    public void Expirar()
    {
        if (Status != StatusInscricao.PendentePagamento)
        {
            throw new RegraDeNegocioException("INSCRICAO_NAO_PENDENTE", "Inscrição não pendente",
                "Somente uma inscrição pendente de pagamento pode expirar.", 409);
        }

        Status = StatusInscricao.Expirada;
    }

    public void Cancelar()
    {
        if (Status == StatusInscricao.Cancelada)
        {
            throw new RegraDeNegocioException("INSCRICAO_JA_CANCELADA", "Inscrição já cancelada",
                "A inscrição já foi cancelada.", 409);
        }

        if (!Ativa)
        {
            throw new RegraDeNegocioException("INSCRICAO_NAO_ATIVA", "Inscrição não ativa",
                "Somente uma inscrição ativa pode ser cancelada.", 409);
        }

        Status = StatusInscricao.Cancelada;
        ReservadaAte = null;
    }

    /// <summary>RN15: o participante cancela até 24 horas antes do início do evento.</summary>
    public bool DentroDoPrazoDeCancelamento(DateTime dataInicioEvento, DateTime agora) =>
        agora <= dataInicioEvento - AntecedenciaCancelamento;

    /// <summary>RN20: apenas inscrição confirmada, de 2 horas antes do início até o fim do evento, uma única vez.</summary>
    public void RegistrarCheckin(DateTime dataInicioEvento, DateTime dataFimEvento, DateTime agora)
    {
        if (PresencaConfirmada)
        {
            throw new RegraDeNegocioException("CHECKIN_JA_REALIZADO", "Check-in já realizado",
                "O check-in desta inscrição já foi registrado.", 409);
        }

        if (Status != StatusInscricao.Confirmada)
        {
            throw new RegraDeNegocioException("INSCRICAO_NAO_CONFIRMADA", "Inscrição não confirmada",
                "O check-in só é permitido para inscrição confirmada.");
        }

        if (agora < dataInicioEvento - AntecedenciaCheckin || agora > dataFimEvento)
        {
            throw new RegraDeNegocioException("FORA_DO_HORARIO_DE_CHECKIN", "Fora do horário de check-in",
                "O check-in é permitido de 2 horas antes do início até o fim do evento.");
        }

        PresencaConfirmada = true;
        CheckinEm = agora;
    }
}
