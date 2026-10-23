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

    /// <summary>Intervalo da expiração de reservas (RN11) e do reenvio de estornos pendentes.</summary>
    public int IntervaloReservasMinutos { get; set; } = 1;
}

/// <summary>
/// Executa uma tarefa em intervalo fixo, em um escopo próprio. Uma falha (ex.: banco indisponível) é registrada e
/// a tarefa tenta de novo no ciclo seguinte; nunca derruba a aplicação. As tarefas são idempotentes.
/// </summary>
public abstract class TarefaPeriodicaWorker(IServiceScopeFactory escopos, ILogger logger) : BackgroundService
{
    protected abstract string Nome { get; }

    protected abstract bool Habilitada { get; }

    protected abstract TimeSpan Intervalo { get; }

    protected abstract Task ExecutarAsync(IServiceProvider servicos, CancellationToken cancellationToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!Habilitada)
        {
            return;
        }

        using var temporizador = new PeriodicTimer(Intervalo);

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
            await ExecutarAsync(escopo.ServiceProvider, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha na tarefa periódica {Tarefa}.", Nome);
        }
    }
}

/// <summary>Encerra os eventos publicados cujo término já passou (RN22), a cada 15 minutos.</summary>
public class EncerrarEventosWorker(IServiceScopeFactory escopos, IOptions<TarefasOptions> opcoes, ILogger<EncerrarEventosWorker> logger)
    : TarefaPeriodicaWorker(escopos, logger)
{
    private readonly ILogger _logger = logger;

    protected override string Nome => "encerramento de eventos";

    protected override bool Habilitada => opcoes.Value.Habilitar;

    protected override TimeSpan Intervalo => TimeSpan.FromMinutes(Math.Max(1, opcoes.Value.IntervaloEncerramentoMinutos));

    protected override async Task ExecutarAsync(IServiceProvider servicos, CancellationToken cancellationToken)
    {
        var encerrados = await servicos.GetRequiredService<EncerramentoEventosService>().EncerrarVencidosAsync(cancellationToken);
        if (encerrados > 0)
        {
            _logger.LogInformation("{Quantidade} evento(s) encerrado(s) automaticamente.", encerrados);
        }
    }
}

/// <summary>Expira reservas vencidas (RN11) e repete estornos que o gateway recusou, a cada minuto.</summary>
public class ReservasEEstornosWorker(IServiceScopeFactory escopos, IOptions<TarefasOptions> opcoes, ILogger<ReservasEEstornosWorker> logger)
    : TarefaPeriodicaWorker(escopos, logger)
{
    private readonly ILogger _logger = logger;

    protected override string Nome => "reservas e estornos";

    protected override bool Habilitada => opcoes.Value.Habilitar;

    protected override TimeSpan Intervalo => TimeSpan.FromMinutes(Math.Max(1, opcoes.Value.IntervaloReservasMinutos));

    protected override async Task ExecutarAsync(IServiceProvider servicos, CancellationToken cancellationToken)
    {
        var expiradas = await servicos.GetRequiredService<ExpiracaoReservasService>().ExpirarVencidasAsync(cancellationToken);
        var estornados = await servicos.GetRequiredService<EstornoService>().ProcessarPendentesAsync(null, cancellationToken);

        if (expiradas > 0 || estornados > 0)
        {
            _logger.LogInformation("{Expiradas} reserva(s) expirada(s); {Estornados} estorno(s) concluído(s).", expiradas, estornados);
        }
    }
}
