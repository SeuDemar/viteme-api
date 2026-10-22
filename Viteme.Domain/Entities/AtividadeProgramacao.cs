using Viteme.Domain.Excecoes;

namespace Viteme.Domain.Entities;

/// <summary>Item da programação do evento.</summary>
public class AtividadeProgramacao
{
    private AtividadeProgramacao()
    {
    }

    public AtividadeProgramacao(Guid eventoId, string titulo, string? palestrante, string? local, DateTime inicio, DateTime fim)
    {
        Id = Guid.NewGuid();
        EventoId = eventoId;
        Definir(titulo, palestrante, local, inicio, fim);
    }

    public Guid Id { get; private set; }

    public Guid EventoId { get; private set; }

    public string Titulo { get; private set; } = null!;

    public string? Palestrante { get; private set; }

    public string? Local { get; private set; }

    public DateTime Inicio { get; private set; }

    public DateTime Fim { get; private set; }

    public void Definir(string titulo, string? palestrante, string? local, DateTime inicio, DateTime fim)
    {
        if (fim <= inicio)
        {
            throw new RegraDeNegocioException("PERIODO_INVALIDO", "Período inválido",
                "O término da atividade deve ser posterior ao início.", 400);
        }

        Titulo = titulo;
        Palestrante = palestrante;
        Local = local;
        Inicio = inicio;
        Fim = fim;
    }
}
