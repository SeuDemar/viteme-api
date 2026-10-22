namespace Viteme.Domain.Entities;

/// <summary>Evento acompanhado por um usuário, que recebe avisos sem estar inscrito.</summary>
public class InteresseEvento
{
    private InteresseEvento()
    {
    }

    public InteresseEvento(Guid usuarioId, Guid eventoId)
    {
        UsuarioId = usuarioId;
        EventoId = eventoId;
    }

    public Guid UsuarioId { get; private set; }

    public Guid EventoId { get; private set; }

    public DateTime CriadoEm { get; private set; }
}
