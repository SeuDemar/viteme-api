using Viteme.Domain.Enums;

namespace Viteme.Application.DTOs.Pagamentos;

/// <summary>E29 — cobrança pendente existente (200) ou nova (201).</summary>
public sealed record CobrancaResponse(Guid PagamentoId, StatusPagamento Status, decimal Valor, string? UrlPagamento);

public sealed record CobrancaResultado(CobrancaResponse Cobranca, bool Nova);

/// <summary>E30 — situação do pagamento, consultada pelo frontend ao voltar do gateway.</summary>
public sealed record PagamentoStatusResponse(
    StatusPagamento Status, decimal Valor, string? Metodo, DateTime AtualizadoEm, StatusInscricao StatusInscricao);

/// <summary>E46 — Pix da cobrança pendente: "copia e cola" e QR Code em PNG (base64, sem prefixo <c>data:</c>).</summary>
public sealed record PixResponse(string CopiaECola, string ImagemBase64, DateTime? ExpiraEm);

/// <summary>
/// E47 — totais das inscrições pagas do evento, em reais e brutos (antes das taxas do meio de pagamento).
/// <c>Recebido</c> = pagamentos aprovados; desses, <c>Retido</c> está na garantia (RN26) e <c>Liberado</c> já está disponível
/// ao organizador. <c>Estornado</c> foi devolvido; <c>AguardandoPagamento</c> são cobranças ainda pendentes.
/// </summary>
public sealed record RecebimentosEventoResponse(
    decimal Recebido,
    decimal Retido,
    decimal Liberado,
    decimal Estornado,
    decimal AguardandoPagamento,
    int PagamentosAprovados,
    int PagamentosEstornados);

/// <summary>E31 — <c>processado</c> é falso quando o webhook já havia sido tratado antes.</summary>
public sealed record WebhookResponse(bool Processado);
