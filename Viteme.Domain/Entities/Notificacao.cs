using Viteme.Domain.Enums;

namespace Viteme.Domain.Entities;

/// <summary>Aviso exibido no sino do site. Toda notificação é registrada aqui (RN16).</summary>
public class Notificacao
{
    private Notificacao()
    {
    }

    public Notificacao(Guid usuarioId, Guid? eventoId, TipoNotificacao tipo, string titulo, string mensagem)
    {
        Id = Guid.NewGuid();
        UsuarioId = usuarioId;
        EventoId = eventoId;
        Tipo = tipo;
        Titulo = titulo;
        Mensagem = mensagem;
    }

    public Guid Id { get; private set; }

    public Guid UsuarioId { get; private set; }

    public Guid? EventoId { get; private set; }

    public TipoNotificacao Tipo { get; private set; }

    public string Titulo { get; private set; } = null!;

    public string Mensagem { get; private set; } = null!;

    public bool Lida { get; private set; }

    public DateTime CriadoEm { get; private set; }

    public EnvioSms? EnvioSms { get; private set; }

    public void MarcarComoLida() => Lida = true;

    /// <summary>RN16: registra o envio por SMS desta notificação (feito depois, por tarefa, com novas tentativas se falhar).</summary>
    public void AgendarSms(string telefone) => EnvioSms ??= new EnvioSms(Id, telefone);
}
