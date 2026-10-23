using Viteme.Application.DTOs.Eventos;
using Viteme.Application.Excecoes;
using Viteme.Application.Interfaces;
using Viteme.Domain.Entities;
using Viteme.Domain.Enums;
using Viteme.Domain.Excecoes;

namespace Viteme.Application.Services;

/// <summary>Colaboradores do evento (E21–E23). Regras RN04, RN07 e RN08.</summary>
public class ColaboradorService(
    IEventoRepository eventos,
    IColaboradorRepository colaboradores,
    IUsuarioRepository usuarios,
    IInscricaoRepository inscricoes,
    NotificacaoService notificacoes,
    IUnitOfWork unitOfWork,
    IRelogio relogio)
{
    public async Task<IReadOnlyList<ColaboradorResponse>> ListarAsync(Guid eventoId, CancellationToken cancellationToken = default)
    {
        _ = await eventos.ObterPorIdAsync(eventoId, cancellationToken) ?? throw EventoService.EventoNaoEncontrado();

        return (await colaboradores.ListarAsync(eventoId, cancellationToken))
            .Select(c => new ColaboradorResponse(c.UsuarioId, c.Nome, c.Email, c.AdicionadoEm))
            .ToList();
    }

    public async Task<ColaboradorResponse> AdicionarAsync(Guid eventoId, ColaboradorRequest requisicao, CancellationToken cancellationToken = default)
    {
        var evento = await eventos.ObterPorIdAsync(eventoId, cancellationToken) ?? throw EventoService.EventoNaoEncontrado();

        var usuario = await usuarios.ObterPorEmailAsync(requisicao.Email.Trim(), cancellationToken)
            ?? throw new RegraDeNegocioException("USUARIO_NAO_ENCONTRADO", "Usuário não encontrado",
                "Não existe usuário cadastrado com este e-mail.", 404);

        // RN04: o organizador não pode ser colaborador do próprio evento.
        if (usuario.Id == evento.OrganizadorId)
        {
            throw new RegraDeNegocioException("ORGANIZADOR_NAO_PODE_SER_COLABORADOR", "Organizador não pode ser colaborador",
                "O organizador já tem acesso total ao evento.");
        }

        if (await colaboradores.ObterAsync(eventoId, usuario.Id, cancellationToken) is not null)
        {
            throw JaColaborador();
        }

        // RN08: quem tem inscrição ativa no evento não pode colaborar nele.
        if (await inscricoes.ObterAtivaAsync(eventoId, usuario.Id, cancellationToken) is not null)
        {
            throw new RegraDeNegocioException("USUARIO_JA_INSCRITO", "Usuário já inscrito",
                "Quem tem inscrição ativa no evento não pode ser colaborador. Cancele a inscrição antes.");
        }

        var colaborador = new ColaboradorEvento(eventoId, usuario.Id, relogio.UtcAgora);
        colaboradores.Adicionar(colaborador);

        await notificacoes.NotificarAsync(
            [usuario.Id], eventoId, TipoNotificacao.Colaboracao,
            "Você foi adicionado como colaborador",
            $"Você agora colabora no evento \"{evento.Titulo}\": pode consultar os inscritos e fazer o check-in.",
            cancellationToken);

        try
        {
            await unitOfWork.SalvarAsync(cancellationToken);
        }
        catch (ViolacaoDeUnicidadeException)
        {
            throw JaColaborador();
        }

        return new ColaboradorResponse(usuario.Id, usuario.Nome, usuario.Email, colaborador.AdicionadoEm);
    }

    public async Task RemoverAsync(Guid eventoId, Guid usuarioId, CancellationToken cancellationToken = default)
    {
        _ = await eventos.ObterPorIdAsync(eventoId, cancellationToken) ?? throw EventoService.EventoNaoEncontrado();

        var colaborador = await colaboradores.ObterAsync(eventoId, usuarioId, cancellationToken)
            ?? throw new RegraDeNegocioException("COLABORADOR_NAO_ENCONTRADO", "Colaborador não encontrado",
                "Este usuário não é colaborador do evento.", 404);

        colaboradores.Remover(colaborador);
        await unitOfWork.SalvarAsync(cancellationToken);
    }

    private static RegraDeNegocioException JaColaborador() =>
        new("JA_COLABORADOR", "Já é colaborador", "Este usuário já é colaborador do evento.", 409);
}
