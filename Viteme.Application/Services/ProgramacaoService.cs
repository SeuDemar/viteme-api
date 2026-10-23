using Viteme.Application.DTOs.Eventos;
using Viteme.Application.Interfaces;
using Viteme.Domain.Entities;
using Viteme.Domain.Enums;
using Viteme.Domain.Excecoes;

namespace Viteme.Application.Services;

/// <summary>Programação do evento (E18–E20). Regras RN05, RN07 e RN13: em evento publicado, mudanças exigem motivo e avisam os inscritos.</summary>
public class ProgramacaoService(
    IEventoRepository eventos,
    AlteracaoEventoRegistrador registrador,
    IUnitOfWork unitOfWork)
{
    public async Task<AtividadeResponse> AdicionarAsync(
        Guid eventoId, Guid usuarioId, AtividadeRequest requisicao, CancellationToken cancellationToken = default)
    {
        var evento = await ObterEventoAsync(eventoId, cancellationToken);
        var (inicio, fim) = ValidarPeriodo(evento, requisicao);
        var motivo = ExigirMotivoSePublicado(evento, requisicao.MotivoAlteracao);

        var atividade = new AtividadeProgramacao(evento.Id, requisicao.Titulo.Trim(), Limpar(requisicao.Palestrante), Limpar(requisicao.Local), inicio, fim);
        eventos.Adicionar(atividade);

        await registrador.RegistrarAsync(evento, usuarioId,
            Publicado(evento) ? [new MudancaEvento("programacao", null, Descrever(atividade))] : [],
            motivo ?? string.Empty, cancellationToken);

        await unitOfWork.SalvarAsync(cancellationToken);
        return EventoService.Mapear(atividade);
    }

    public async Task<AtividadeResponse> EditarAsync(
        Guid eventoId, Guid atividadeId, Guid usuarioId, AtividadeRequest requisicao, CancellationToken cancellationToken = default)
    {
        var evento = await ObterEventoAsync(eventoId, cancellationToken);
        var atividade = await ObterAtividadeAsync(eventoId, atividadeId, cancellationToken);
        var (inicio, fim) = ValidarPeriodo(evento, requisicao);
        var motivo = ExigirMotivoSePublicado(evento, requisicao.MotivoAlteracao);

        var anterior = Descrever(atividade);
        atividade.Definir(requisicao.Titulo.Trim(), Limpar(requisicao.Palestrante), Limpar(requisicao.Local), inicio, fim);

        await registrador.RegistrarAsync(evento, usuarioId,
            Publicado(evento) ? [new MudancaEvento("programacao", anterior, Descrever(atividade))] : [],
            motivo ?? string.Empty, cancellationToken);

        await unitOfWork.SalvarAsync(cancellationToken);
        return EventoService.Mapear(atividade);
    }

    public async Task RemoverAsync(
        Guid eventoId, Guid atividadeId, Guid usuarioId, string? motivoInformado, CancellationToken cancellationToken = default)
    {
        var evento = await ObterEventoAsync(eventoId, cancellationToken);
        var atividade = await ObterAtividadeAsync(eventoId, atividadeId, cancellationToken);
        var motivo = ExigirMotivoSePublicado(evento, motivoInformado);

        eventos.Remover(atividade);

        await registrador.RegistrarAsync(evento, usuarioId,
            Publicado(evento) ? [new MudancaEvento("programacao", Descrever(atividade), null)] : [],
            motivo ?? string.Empty, cancellationToken);

        await unitOfWork.SalvarAsync(cancellationToken);
    }

    private async Task<Evento> ObterEventoAsync(Guid eventoId, CancellationToken cancellationToken)
    {
        var evento = await eventos.ObterPorIdAsync(eventoId, cancellationToken) ?? throw EventoService.EventoNaoEncontrado();
        evento.GarantirNaoFinalizado();
        return evento;
    }

    private async Task<AtividadeProgramacao> ObterAtividadeAsync(Guid eventoId, Guid atividadeId, CancellationToken cancellationToken) =>
        await eventos.ObterAtividadeAsync(eventoId, atividadeId, cancellationToken)
        ?? throw new RegraDeNegocioException("ATIVIDADE_NAO_ENCONTRADA", "Atividade não encontrada",
            "A atividade não existe neste evento.", 404);

    /// <summary>RN05: a atividade deve estar dentro do período do evento.</summary>
    private static (DateTime Inicio, DateTime Fim) ValidarPeriodo(Evento evento, AtividadeRequest requisicao)
    {
        var inicio = requisicao.Inicio!.Value.UtcDateTime;
        var fim = requisicao.Fim!.Value.UtcDateTime;

        if (inicio < evento.DataInicio || fim > evento.DataFim)
        {
            throw new RegraDeNegocioException("ATIVIDADE_FORA_DO_PERIODO", "Atividade fora do período",
                "A atividade deve acontecer dentro do período do evento.");
        }

        return (inicio, fim);
    }

    private static bool Publicado(Evento evento) => evento.Status is StatusEvento.Publicado or StatusEvento.Oculto;

    private static string? ExigirMotivoSePublicado(Evento evento, string? motivo)
    {
        if (!Publicado(evento))
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(motivo) ? throw EventoService.MotivoObrigatorio() : motivo.Trim();
    }

    private static string Descrever(AtividadeProgramacao a) =>
        $"{a.Titulo} ({AlteracaoEventoRegistrador.Formatar(a.Inicio)} a {AlteracaoEventoRegistrador.Formatar(a.Fim)})";

    private static string? Limpar(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
}
