using Viteme.Application.Excecoes;
using Viteme.Application.Interfaces;
using Viteme.Domain.Entities;
using Viteme.Domain.Enums;
using Viteme.Domain.Excecoes;

namespace Viteme.Application.Services;

/// <summary>Acompanhar um evento sem estar inscrito (E32, E33): o usuário recebe avisos de alteração e lembretes (RN13, RN17).</summary>
public class InteresseService(IEventoRepository eventos, IInteresseRepository interesses, IUnitOfWork unitOfWork)
{
    /// <summary>Idempotente. Só evento publicado pode ser acompanhado; quem não pode ver o evento recebe 404.</summary>
    public async Task SeguirAsync(Guid eventoId, Guid usuarioId, CancellationToken cancellationToken = default)
    {
        var permissao = await eventos.ObterPermissaoAsync(eventoId, usuarioId, cancellationToken);
        if (!permissao.PodeVer)
        {
            throw EventoService.EventoNaoEncontrado();
        }

        if (permissao.Status != StatusEvento.Publicado)
        {
            throw new RegraDeNegocioException("EVENTO_INDISPONIVEL", "Evento indisponível",
                "Só é possível acompanhar eventos publicados.");
        }

        if (await interesses.ExisteAsync(eventoId, usuarioId, cancellationToken))
        {
            return;
        }

        interesses.Adicionar(new InteresseEvento(usuarioId, eventoId));

        try
        {
            await unitOfWork.SalvarAsync(cancellationToken);
        }
        catch (ViolacaoDeUnicidadeException)
        {
            // Dois pedidos simultâneos: o outro já registrou o interesse, e o resultado é o mesmo.
        }
    }

    /// <summary>Idempotente.</summary>
    public async Task DeixarDeSeguirAsync(Guid eventoId, Guid usuarioId, CancellationToken cancellationToken = default)
    {
        var interesse = await interesses.ObterAsync(eventoId, usuarioId, cancellationToken);
        if (interesse is null)
        {
            return;
        }

        interesses.Remover(interesse);
        await unitOfWork.SalvarAsync(cancellationToken);
    }
}
