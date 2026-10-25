namespace Viteme.Application.Interfaces;

/// <summary>Abstrai o tempo para testar prazos (reserva de 30 min, cancelamento em 24 h, validade de códigos).</summary>
public interface IRelogio
{
    /// <summary>Instante atual em UTC (<see cref="DateTimeKind.Utc"/>).</summary>
    DateTime UtcAgora { get; }
}
