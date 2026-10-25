using Viteme.Domain.Enums;
using Viteme.Domain.Excecoes;

namespace Viteme.Domain.Entities;

/// <summary>Denúncia de um evento indevido. Cada usuário denuncia um evento uma única vez (RN19).</summary>
public class Denuncia
{
    /// <summary>Número de denúncias que oculta um evento publicado.</summary>
    public const int LimiteParaOcultar = 5;

    private Denuncia()
    {
    }

    public Denuncia(Guid eventoId, Guid usuarioId, MotivoDenuncia motivo, string? descricao)
    {
        if (motivo == MotivoDenuncia.Outro && string.IsNullOrWhiteSpace(descricao))
        {
            throw new RegraDeNegocioException("DESCRICAO_OBRIGATORIA", "Descrição obrigatória",
                "Informe uma descrição quando o motivo for \"Outro\".", 400);
        }

        Id = Guid.NewGuid();
        EventoId = eventoId;
        UsuarioId = usuarioId;
        Motivo = motivo;
        Descricao = descricao;
    }

    public Guid Id { get; private set; }

    public Guid EventoId { get; private set; }

    public Guid UsuarioId { get; private set; }

    public MotivoDenuncia Motivo { get; private set; }

    public string? Descricao { get; private set; }

    public DateTime CriadoEm { get; private set; }
}
