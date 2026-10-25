namespace Viteme.Domain.Entities;

/// <summary>Associação entre um usuário e o evento em que ele colabora (RN04).</summary>
public class ColaboradorEvento
{
    private ColaboradorEvento()
    {
    }

    public ColaboradorEvento(Guid eventoId, Guid usuarioId, DateTime agora)
    {
        Id = Guid.NewGuid();
        EventoId = eventoId;
        UsuarioId = usuarioId;
        AdicionadoEm = agora;
    }

    public Guid Id { get; private set; }

    public Guid EventoId { get; private set; }

    public Guid UsuarioId { get; private set; }

    public DateTime AdicionadoEm { get; private set; }
}
