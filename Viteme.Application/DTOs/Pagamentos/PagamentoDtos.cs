using Viteme.Domain.Enums;

namespace Viteme.Application.DTOs.Pagamentos;

/// <summary>E29 — cobrança pendente existente (200) ou nova (201).</summary>
public sealed record CobrancaResponse(Guid PagamentoId, StatusPagamento Status, decimal Valor, string? UrlPagamento);

public sealed record CobrancaResultado(CobrancaResponse Cobranca, bool Nova);

/// <summary>E30 — situação do pagamento, consultada pelo frontend ao voltar do gateway.</summary>
public sealed record PagamentoStatusResponse(
    StatusPagamento Status, decimal Valor, string? Metodo, DateTime AtualizadoEm, StatusInscricao StatusInscricao);

/// <summary>E31 — <c>processado</c> é falso quando o webhook já havia sido tratado antes.</summary>
public sealed record WebhookResponse(bool Processado);
