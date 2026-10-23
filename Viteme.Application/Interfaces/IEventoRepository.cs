using Viteme.Application.DTOs.Eventos;
using Viteme.Domain.Entities;
using Viteme.Domain.Enums;

namespace Viteme.Application.Interfaces;

/// <summary>Papel e visibilidade de um usuário em um evento. Base das políticas <c>OrganizadorDoEvento</c> e <c>OrganizadorOuColaborador</c>.</summary>
public sealed record PermissaoEvento(bool Existe, StatusEvento Status, PapelEvento Papel, bool Inscrito)
{
    public static PermissaoEvento Inexistente { get; } = new(false, StatusEvento.Rascunho, PapelEvento.Participante, false);

    public bool EhOrganizador => Papel == PapelEvento.Organizador;

    public bool EhOrganizadorOuColaborador => Papel is PapelEvento.Organizador or PapelEvento.Colaborador;

    /// <summary>Rascunho só para organizador e colaboradores; oculto também para inscritos; os demais são públicos (RN06, RN19).</summary>
    public bool PodeVer => Existe && Status switch
    {
        StatusEvento.Rascunho => EhOrganizadorOuColaborador,
        StatusEvento.Oculto => EhOrganizadorOuColaborador || Inscrito,
        _ => true,
    };
}

public sealed record EventoListado(Evento Evento, int VagasOcupadas, bool AlteradoRecentemente);

public sealed record EventoDoUsuario(Evento Evento, PapelEvento Papel);

public interface IEventoRepository
{
    /// <summary>Carrega o evento com as atividades da programação (com rastreamento, para edição).</summary>
    Task<Evento?> ObterComAtividadesAsync(Guid eventoId, CancellationToken cancellationToken = default);

    Task<Evento?> ObterPorIdAsync(Guid eventoId, CancellationToken cancellationToken = default);

    Task<PermissaoEvento> ObterPermissaoAsync(Guid eventoId, Guid? usuarioId, CancellationToken cancellationToken = default);

    /// <summary>Eventos publicados e ainda não encerrados, com filtros e paginação.</summary>
    Task<(IReadOnlyList<EventoListado> Itens, int Total)> ListarMuralAsync(
        MuralFiltro filtro, DateTime agora, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EventoDoUsuario>> ListarDoUsuarioAsync(
        Guid usuarioId, PapelEvento? papel, StatusEvento? status, CancellationToken cancellationToken = default);

    /// <summary>Vagas ocupadas: inscrições confirmadas mais as pendentes com reserva válida (RN09).</summary>
    Task<int> ContarVagasOcupadasAsync(Guid eventoId, DateTime agora, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AlteracaoEvento>> ListarAlteracoesAsync(
        Guid eventoId, int? limite, CancellationToken cancellationToken = default);

    /// <summary>Usuários a avisar sobre mudanças: inscritos ativos, interessados e, se pedido, colaboradores. Sem repetição.</summary>
    Task<IReadOnlyList<Guid>> ListarDestinatariosAsync(
        Guid eventoId, bool incluirColaboradores, CancellationToken cancellationToken = default);

    Task<AtividadeProgramacao?> ObterAtividadeAsync(Guid eventoId, Guid atividadeId, CancellationToken cancellationToken = default);

    /// <summary>Eventos publicados cujo término já passou (RN22).</summary>
    Task<IReadOnlyList<Evento>> ListarVencidosParaEncerrarAsync(DateTime agora, CancellationToken cancellationToken = default);

    /// <summary>Informa a versão (<c>xmin</c>) lida pelo cliente; o salvamento falha se o registro mudou (RI11).</summary>
    void DefinirVersaoEsperada(Evento evento, uint versao);

    void Adicionar(Evento evento);

    void Adicionar(AlteracaoEvento alteracao);

    void Adicionar(AtividadeProgramacao atividade);

    void Remover(AtividadeProgramacao atividade);
}
