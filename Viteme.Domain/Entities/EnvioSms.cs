using Viteme.Domain.Enums;

namespace Viteme.Domain.Entities;

/// <summary>Registro do envio por SMS de uma notificação. Falhas geram até 3 novas tentativas (RN16).</summary>
public class EnvioSms
{
    /// <summary>1 envio inicial + 3 novas tentativas.</summary>
    public const short MaxTentativas = 4;

    private EnvioSms()
    {
    }

    public EnvioSms(Guid notificacaoId, string telefone)
    {
        Id = Guid.NewGuid();
        NotificacaoId = notificacaoId;
        Telefone = telefone;
        Status = StatusSms.Pendente;
    }

    public Guid Id { get; private set; }

    public Guid NotificacaoId { get; private set; }

    public string Telefone { get; private set; } = null!;

    public StatusSms Status { get; private set; }

    public short Tentativas { get; private set; }

    public string? UltimoErro { get; private set; }

    public DateTime? EnviadoEm { get; private set; }

    public DateTime CriadoEm { get; private set; }

    /// <summary>Ainda há tentativa disponível e o SMS não foi enviado.</summary>
    public bool PodeReenviar => Status != StatusSms.Enviado && Tentativas < MaxTentativas;

    public void RegistrarSucesso(DateTime agora)
    {
        Tentativas++;
        Status = StatusSms.Enviado;
        EnviadoEm = agora;
        UltimoErro = null;
    }

    /// <summary>O status só vira FALHOU quando as tentativas se esgotam; antes disso continua PENDENTE para reenvio.</summary>
    public void RegistrarFalha(string erro)
    {
        Tentativas++;
        UltimoErro = erro.Length > 300 ? erro[..300] : erro;
        Status = Tentativas >= MaxTentativas ? StatusSms.Falhou : StatusSms.Pendente;
    }
}
