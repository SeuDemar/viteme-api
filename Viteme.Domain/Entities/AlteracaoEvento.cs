namespace Viteme.Domain.Entities;

/// <summary>Histórico de alterações de um evento publicado, base do aviso de alteração (RN13).</summary>
public class AlteracaoEvento
{
    private AlteracaoEvento()
    {
    }

    public AlteracaoEvento(Guid eventoId, Guid autorId, string campo, string? valorAnterior, string? valorNovo, string motivo)
    {
        Id = Guid.NewGuid();
        EventoId = eventoId;
        AutorId = autorId;
        Campo = campo;
        ValorAnterior = valorAnterior;
        ValorNovo = valorNovo;
        Motivo = motivo;
    }

    public Guid Id { get; private set; }

    public Guid EventoId { get; private set; }

    public Guid AutorId { get; private set; }

    public string Campo { get; private set; } = null!;

    public string? ValorAnterior { get; private set; }

    public string? ValorNovo { get; private set; }

    public string Motivo { get; private set; } = null!;

    public DateTime CriadoEm { get; private set; }
}
