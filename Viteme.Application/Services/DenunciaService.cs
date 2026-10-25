using Viteme.Application.DTOs.Denuncias;
using Viteme.Application.Interfaces;
using Viteme.Domain.Entities;
using Viteme.Domain.Enums;
using Viteme.Domain.Excecoes;

namespace Viteme.Application.Services;

/// <summary>
/// Denúncia de eventos indevidos (E37). RN19: sem administrador, o próprio volume de denúncias protege a plataforma.
/// Com 5 denúncias o evento publicado sai do mural (OCULTO). Se o organizador republicar (uma única vez) e o evento
/// receber outras 5, ele é cancelado automaticamente (RN14).
/// </summary>
public class DenunciaService(
    IEventoRepository eventos,
    IDenunciaRepository denuncias,
    IInscricaoRepository inscricoes,
    CancelamentoEventoService cancelamento,
    EstornoService estornos,
    NotificacaoService notificacoes,
    IUnitOfWork unitOfWork)
{
    private enum Consequencia
    {
        Nenhuma,
        Ocultado,
        Cancelado,
    }

    public async Task DenunciarAsync(Guid eventoId, Guid usuarioId, DenunciarEventoRequest requisicao, CancellationToken cancellationToken = default)
    {
        var consequencia = await unitOfWork.ExecutarEmTransacaoAsync(
            c => RegistrarAsync(eventoId, usuarioId, requisicao, c), cancellationToken);

        if (consequencia == Consequencia.Cancelado)
        {
            // Depois do commit: o gateway nunca é chamado dentro da transação do banco.
            await estornos.ProcessarPendentesAsync(eventoId, cancellationToken);
        }
    }

    private async Task<Consequencia> RegistrarAsync(
        Guid eventoId, Guid usuarioId, DenunciarEventoRequest requisicao, CancellationToken cancellationToken)
    {
        // Bloqueia o evento: denúncias simultâneas são contadas uma de cada vez e o limite dispara uma única vez.
        _ = await eventos.ObterBloqueadoAsync(eventoId, cancellationToken) ?? throw EventoService.EventoNaoEncontrado();

        var permissao = await eventos.ObterPermissaoAsync(eventoId, usuarioId, cancellationToken);
        if (!permissao.PodeVer)
        {
            throw EventoService.EventoNaoEncontrado();
        }

        if (permissao.EhOrganizadorOuColaborador)
        {
            throw new RegraDeNegocioException("NAO_PODE_DENUNCIAR_PROPRIO_EVENTO", "Não é possível denunciar o próprio evento",
                "Organizador e colaboradores não denunciam o evento em que atuam.");
        }

        if (permissao.Status is not (StatusEvento.Publicado or StatusEvento.Oculto))
        {
            throw new RegraDeNegocioException("EVENTO_INDISPONIVEL", "Evento indisponível",
                "Só eventos publicados podem ser denunciados.");
        }

        if (await denuncias.ExisteAsync(eventoId, usuarioId, cancellationToken))
        {
            throw new RegraDeNegocioException("DENUNCIA_EXISTENTE", "Denúncia já registrada",
                "Você já denunciou este evento.", 409);
        }

        var evento = await eventos.ObterPorIdAsync(eventoId, cancellationToken) ?? throw EventoService.EventoNaoEncontrado();
        denuncias.Adicionar(new Denuncia(eventoId, usuarioId, requisicao.Motivo!.Value, requisicao.Descricao?.Trim()));
        await unitOfWork.SalvarAsync(cancellationToken);

        // Depois de uma republicação só contam as denúncias novas.
        var total = await denuncias.ContarAsync(eventoId, evento.ReabertoEm, cancellationToken);
        var consequencia = Consequencia.Nenhuma;

        if (total >= Denuncia.LimiteParaOcultar && evento.Status == StatusEvento.Publicado)
        {
            if (evento.ReabertoEm is null)
            {
                evento.Ocultar();
                var destinatarios = (await inscricoes.ListarUsuariosAtivosAsync(eventoId, cancellationToken))
                    .Append(evento.OrganizadorId);

                await notificacoes.NotificarAsync(
                    destinatarios, eventoId, TipoNotificacao.Denuncia, "Evento oculto por denúncias",
                    $"O evento \"{evento.Titulo}\" recebeu {Denuncia.LimiteParaOcultar} denúncias e saiu do mural. "
                    + "Quem já está inscrito continua com acesso. O organizador pode corrigir o evento e republicá-lo uma vez.",
                    cancellationToken);
                consequencia = Consequencia.Ocultado;
            }
            else
            {
                await cancelamento.CancelarAsync(
                    evento,
                    $"O evento \"{evento.Titulo}\" foi cancelado automaticamente por receber novas denúncias depois de republicado.",
                    autorId: null, cancellationToken);
                consequencia = Consequencia.Cancelado;
            }
        }

        await unitOfWork.SalvarAsync(cancellationToken);
        return consequencia;
    }
}
