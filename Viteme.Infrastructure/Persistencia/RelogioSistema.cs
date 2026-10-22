using Viteme.Application.Interfaces;

namespace Viteme.Infrastructure.Persistencia;

public class RelogioSistema : IRelogio
{
    public DateTime UtcAgora => DateTime.UtcNow;
}
