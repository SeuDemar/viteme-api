using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Viteme.Application.Interfaces;
using Viteme.Domain.Enums;

namespace Viteme.Infrastructure.Externos;

/// <summary>
/// Subconta e garantia simuladas, para desenvolvimento e testes com <c>Gateway:Provedor=Stub</c>: a subconta é aprovada
/// na hora e a garantia é registrada e liberada sem chamar ninguém. Segue as mesmas chaves de <see cref="GatewayAsaasOptions"/>.
/// </summary>
public sealed class ContasRecebimentoStub(IOptions<GatewayAsaasOptions> opcoes, ILogger<ContasRecebimentoStub> logger) : IContasRecebimento
{
    public bool SubcontasHabilitadas => opcoes.Value.Subcontas.Habilitar;

    public bool GarantiaHabilitada => opcoes.Value.Subcontas.Habilitar && opcoes.Value.Escrow.Habilitar;

    public Task<SubcontaCriada> CriarSubcontaAsync(DadosSubconta dados, CancellationToken cancellationToken = default)
    {
        var sufixo = Guid.NewGuid().ToString("N")[..12];
        logger.LogInformation("[Stub] subconta simulada acc_stub_{Sufixo} criada para o usuário {UsuarioId}.", sufixo, dados.UsuarioId);
        return Task.FromResult(new SubcontaCriada($"acc_stub_{sufixo}", $"wallet_stub_{sufixo}", $"chave_stub_{sufixo}", null));
    }

    public Task<SituacaoSubconta> ConsultarSituacaoAsync(string chaveApiSubconta, CancellationToken cancellationToken = default) =>
        Task.FromResult(new SituacaoSubconta(SituacaoContaRecebimento.Aprovada, null));

    public NotificacaoConta? InterpretarWebhookConta(string corpoBruto) => null;

    public Task HabilitarGarantiaAsync(string contaId, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<string?> ObterGarantiaAsync(string idTransacao, string chaveApiSubconta, CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>($"esc_stub_{idTransacao}");

    public Task EncerrarGarantiaAsync(string idTransacao, string garantiaId, string chaveApiSubconta, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("[Stub] garantia {GarantiaId} liberada.", garantiaId);
        return Task.CompletedTask;
    }
}
