using System.ComponentModel.DataAnnotations;
using Viteme.Domain.Enums;

namespace Viteme.Application.DTOs.Notificacoes;

/// <summary>E34 — filtros e paginação das notificações do sino.</summary>
public sealed class NotificacoesFiltro
{
    /// <summary><c>true</c> só as lidas; <c>false</c> só as não lidas; vazio, todas.</summary>
    public bool? Lidas { get; init; }

    [Range(1, int.MaxValue, ErrorMessage = "A página deve ser 1 ou maior.")]
    public int Pagina { get; init; } = 1;

    [Range(1, 50, ErrorMessage = "O tamanho da página deve estar entre 1 e 50.")]
    public int TamanhoPagina { get; init; } = 20;
}

public sealed record NotificacaoResponse(
    Guid Id, TipoNotificacao Tipo, string Titulo, string Mensagem, Guid? EventoId, bool Lida, DateTime CriadoEm);

/// <summary><c>naoLidas</c> conta todas as não lidas do usuário, independentemente do filtro (selo do sino).</summary>
public sealed record NotificacoesPaginaResponse(IReadOnlyList<NotificacaoResponse> Itens, int NaoLidas, int Pagina, int Total);
