using Microsoft.EntityFrameworkCore;
using Viteme.Application.Interfaces;
using Viteme.Domain.Entities;
using Viteme.Domain.Enums;

namespace Viteme.Infrastructure.Persistencia.Repositorios;

public class EnvioSmsRepository(VitemeDbContext contexto) : IEnvioSmsRepository
{
    public async Task<IReadOnlyList<Guid>> ListarIdsPendentesAsync(int limite, CancellationToken cancellationToken = default) =>
        await contexto.EnviosSms.AsNoTracking()
            .Where(e => e.Status == StatusSms.Pendente && e.Tentativas < EnvioSms.MaxTentativas)
            .OrderBy(e => e.CriadoEm).ThenBy(e => e.Id)
            .Select(e => e.Id)
            .Take(limite)
            .ToListAsync(cancellationToken);

    public async Task<(EnvioSms Envio, Notificacao Notificacao)?> ObterComNotificacaoAsync(
        Guid envioId, CancellationToken cancellationToken = default)
    {
        var envio = await contexto.EnviosSms.FirstOrDefaultAsync(e => e.Id == envioId, cancellationToken);
        if (envio is null)
        {
            return null;
        }

        var notificacao = await contexto.Notificacoes.FirstAsync(n => n.Id == envio.NotificacaoId, cancellationToken);
        return (envio, notificacao);
    }
}
