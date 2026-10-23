using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Viteme.Application.Services;

namespace Viteme.Infrastructure.Tarefas;

/// <summary>Seção <c>Tarefas</c> da configuração.</summary>
public class TarefasOptions
{
    public const string Secao = "Tarefas";

    public bool Habilitar { get; set; } = true;

    /// <summary>Intervalo do encerramento automático de eventos (RN22).</summary>
    public int IntervaloEncerramentoMinutos { get; set; } = 15;
}

/// <summary>Encerra periodicamente os eventos publicados cujo término já passou (RN22).</summary>
public class EncerrarEventosWorker(
    IServiceScopeFactory escopos, IOptions<TarefasOptions> opcoes, ILogger<EncerrarEventosWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!opcoes.Value.Habilitar)
        {
            return;
        }

        using var temporizador = new PeriodicTimer(TimeSpan.FromMinutes(Math.Max(1, opcoes.Value.IntervaloEncerramentoMinutos)));

        try
        {
            do
            {
                await ExecutarUmaVezAsync(stoppingToken);
            }
            while (await temporizador.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // Aplicação encerrando.
        }
    }

    private async Task ExecutarUmaVezAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var escopo = escopos.CreateAsyncScope();
            var encerrados = await escopo.ServiceProvider.GetRequiredService<EncerramentoEventosService>()
                .EncerrarVencidosAsync(cancellationToken);

            if (encerrados > 0)
            {
                logger.LogInformation("{Quantidade} evento(s) encerrado(s) automaticamente.", encerrados);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Uma falha (ex.: banco indisponível) não pode derrubar a aplicação; tenta de novo no próximo ciclo.
            logger.LogError(ex, "Falha ao encerrar eventos vencidos.");
        }
    }
}
