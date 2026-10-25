using Viteme.Application.Interfaces;

namespace Viteme.Tests.Suporte;

/// <summary>Relógio controlável para testar prazos sem esperar o tempo real.</summary>
public class RelogioFixo(DateTime? inicial = null) : IRelogio
{
    public DateTime UtcAgora { get; private set; } = inicial ?? new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);

    public void Avancar(TimeSpan tempo) => UtcAgora += tempo;
}
