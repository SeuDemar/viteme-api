using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Viteme.Application.Interfaces;
using Viteme.Domain.Enums;

namespace Viteme.Infrastructure.Externos;

/// <summary>
/// Subconta do organizador e Conta Escrow no Asaas (Fases 2 e 3). Os pontos marcados com <c>confirmar Qn</c> seguem a
/// documentação pública e precisam ser conferidos na conta sandbox PJ (plano pagamentos-asaas.md, seções 8.4 e 8.5).
/// </summary>
public sealed class ContasRecebimentoAsaas : IContasRecebimento, IDisposable
{
    private readonly ApiAsaas _api;
    private readonly GatewayAsaasOptions _opcoes;
    private readonly ILogger<ContasRecebimentoAsaas> _logger;

    public ContasRecebimentoAsaas(IOptions<GatewayAsaasOptions> opcoes, ILogger<ContasRecebimentoAsaas> logger)
        : this(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) }, opcoes, logger)
    {
    }

    public ContasRecebimentoAsaas(HttpMessageHandler handler, IOptions<GatewayAsaasOptions> opcoes, ILogger<ContasRecebimentoAsaas> logger)
    {
        _opcoes = opcoes.Value;
        _logger = logger;
        _api = new ApiAsaas(handler, _opcoes);
    }

    public bool SubcontasHabilitadas => _opcoes.Subcontas.Habilitar;

    public bool GarantiaHabilitada => _opcoes.Subcontas.Habilitar && _opcoes.Escrow.Habilitar;

    public async Task<SubcontaCriada> CriarSubcontaAsync(DadosSubconta dados, CancellationToken cancellationToken = default)
    {
        // confirmar Q3: campos obrigatórios de POST /v3/accounts para PJ (a documentação pede incomeValue e companyType).
        var corpo = new Dictionary<string, object?>
        {
            ["name"] = dados.RazaoSocial,
            ["email"] = dados.Email,
            ["cpfCnpj"] = dados.Cnpj,
            ["companyType"] = dados.TipoEmpresa,
            ["mobilePhone"] = TelefoneNacional(dados.Telefone),
            ["incomeValue"] = dados.FaturamentoMensal,
            ["postalCode"] = dados.Cep,
            ["address"] = dados.Endereco,
            ["addressNumber"] = dados.Numero,
            ["complement"] = dados.Complemento,
            ["province"] = dados.Bairro,
        };

        // A situação cadastral chega pelo webhook da própria subconta, cadastrado já na criação, com o mesmo token do
        // webhook da conta raiz. Sem URL configurada, só a tarefa periódica atualiza a situação.
        if (!string.IsNullOrWhiteSpace(_opcoes.Subcontas.UrlWebhook))
        {
            corpo["webhooks"] = new[]
            {
                new
                {
                    name = "Viteme - situação da conta",
                    url = _opcoes.Subcontas.UrlWebhook,
                    email = dados.Email,
                    sendType = "SEQUENTIALLY",
                    enabled = true,
                    interrupted = false,
                    authToken = _opcoes.TokenWebhook,
                    // confirmar Q6: nomes dos eventos de situação de conta.
                    events = new[]
                    {
                        "ACCOUNT_STATUS_GENERAL_APPROVAL_APPROVED",
                        "ACCOUNT_STATUS_GENERAL_APPROVAL_REJECTED",
                        "ACCOUNT_STATUS_GENERAL_APPROVAL_PENDING",
                    },
                },
            };
        }

        // A chave de API da subconta só vem nesta resposta. Se a gravação no Viteme falhar depois daqui, a subconta existe
        // no Asaas sem a chave guardada: recuperar pelo painel do Asaas (o reenvio daria CNPJ já cadastrado).
        var criada = await _api.EnviarAsync<ContaAsaas>(HttpMethod.Post, "accounts", corpo, null, cancellationToken);
        if (string.IsNullOrWhiteSpace(criada.Id) || string.IsNullOrWhiteSpace(criada.WalletId) || string.IsNullOrWhiteSpace(criada.ApiKey))
        {
            throw new HttpRequestException("Asaas criou a subconta sem devolver id, walletId ou apiKey.");
        }

        _logger.LogInformation("[Asaas] subconta {ContaId} criada para o usuário {UsuarioId}.", criada.Id, dados.UsuarioId);
        return new SubcontaCriada(criada.Id, criada.WalletId, criada.ApiKey, await ObterUrlOnboardingAsync(criada.ApiKey, cancellationToken));
    }

    public async Task<SituacaoSubconta> ConsultarSituacaoAsync(string chaveApiSubconta, CancellationToken cancellationToken = default)
    {
        var situacao = await _api.EnviarAsync<SituacaoContaAsaas>(HttpMethod.Get, "myAccount/status", null, chaveApiSubconta, cancellationToken);
        var url = await ObterUrlOnboardingAsync(chaveApiSubconta, cancellationToken);
        return new SituacaoSubconta(MapearSituacao(situacao.General), url);
    }

    public NotificacaoConta? InterpretarWebhookConta(string corpoBruto)
    {
        WebhookContaAsaas? dados;
        try
        {
            dados = JsonSerializer.Deserialize<WebhookContaAsaas>(corpoBruto, ApiAsaas.Json);
        }
        catch (JsonException)
        {
            return null;
        }

        if (dados?.Event is not { } evento || !evento.StartsWith("ACCOUNT_STATUS_", StringComparison.Ordinal))
        {
            return null;
        }

        // confirmar Q6: o id em accountStatus é o da subconta (o mesmo devolvido por POST /v3/accounts)?
        if (string.IsNullOrWhiteSpace(dados.AccountStatus?.Id))
        {
            throw new FormatException("O webhook de conta deve informar accountStatus.id.");
        }

        var geral = dados.AccountStatus.General ?? SufixoDaAprovacaoGeral(evento)
            ?? throw new FormatException("O webhook de conta deve informar accountStatus.general.");
        return new NotificacaoConta(dados.AccountStatus.Id, MapearSituacao(geral));
    }

    public async Task HabilitarGarantiaAsync(string contaId, CancellationToken cancellationToken = default)
    {
        // confirmar: POST /v3/accounts/{id}/escrow existe no sandbox (seção 7 do plano). Só recebimentos posteriores ficam retidos.
        await _api.EnviarAsync<JsonElement>(
            HttpMethod.Post, $"accounts/{Uri.EscapeDataString(contaId)}/escrow",
            new
            {
                enabled = true,
                daysToExpire = _opcoes.Escrow.DiasParaExpirar,
                isFeePayer = _opcoes.Escrow.SubcontaPagaMensalidade,
            },
            null, cancellationToken);
        _logger.LogInformation("[Asaas] garantia habilitada na subconta {ContaId}.", contaId);
    }

    public async Task<string?> ObterGarantiaAsync(string idTransacao, string chaveApiSubconta, CancellationToken cancellationToken = default)
    {
        // confirmar Q1/Q2: a garantia do split é consultada com a chave da subconta, pela cobrança da conta raiz?
        var garantia = await ConsultarGarantiaAsync(idTransacao, chaveApiSubconta, cancellationToken);
        return string.IsNullOrWhiteSpace(garantia?.Id) ? null : garantia.Id;
    }

    public async Task EncerrarGarantiaAsync(string idTransacao, string garantiaId, string chaveApiSubconta, CancellationToken cancellationToken = default)
    {
        // Idempotência: uma garantia já encerrada (nova tentativa depois de falha ao gravar) não é encerrada de novo.
        var atual = await ConsultarGarantiaAsync(idTransacao, chaveApiSubconta, cancellationToken);
        if (atual?.FinishDate is not null || atual?.Status is "DONE" or "FINISHED" or "EXPIRED")
        {
            _logger.LogInformation("[Asaas] garantia {GarantiaId} já encerrada ({Status}).", garantiaId, atual?.Status);
            return;
        }

        // confirmar Q1: finish com a chave da subconta ou da raiz.
        await _api.EnviarAsync<JsonElement>(
            HttpMethod.Post, $"escrow/{Uri.EscapeDataString(garantiaId)}/finish", null, chaveApiSubconta, cancellationToken);
        _logger.LogInformation("[Asaas] garantia {GarantiaId} encerrada; valor liberado à subconta.", garantiaId);
    }

    public void Dispose() => _api.Dispose();

    // confirmar Q6: valores de general (APPROVED, REJECTED, PENDING, AWAITING_APPROVAL).
    internal static SituacaoContaRecebimento MapearSituacao(string? geral) => geral switch
    {
        "APPROVED" => SituacaoContaRecebimento.Aprovada,
        "REJECTED" => SituacaoContaRecebimento.Rejeitada,
        _ => SituacaoContaRecebimento.Pendente,
    };

    /// <summary>O Viteme guarda o telefone em E.164 (+5544999990000); o Asaas espera só DDD e número.</summary>
    public static string TelefoneNacional(string telefone)
    {
        var digitos = new string(telefone.Where(char.IsDigit).ToArray());
        return digitos.Length is 12 or 13 && digitos.StartsWith("55", StringComparison.Ordinal) ? digitos[2..] : digitos;
    }

    private static string? SufixoDaAprovacaoGeral(string evento) =>
        evento.StartsWith("ACCOUNT_STATUS_GENERAL_APPROVAL_", StringComparison.Ordinal)
            ? evento["ACCOUNT_STATUS_GENERAL_APPROVAL_".Length..]
            : null;

    private async Task<GarantiaAsaas?> ConsultarGarantiaAsync(string idTransacao, string chave, CancellationToken cancellationToken)
    {
        try
        {
            return await _api.EnviarAsync<GarantiaAsaas>(
                HttpMethod.Get, $"payments/{Uri.EscapeDataString(idTransacao)}/escrow", null, chave, cancellationToken);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <summary>Link do envio de documentos da análise cadastral; melhor esforço (a subconta continua válida sem ele).</summary>
    private async Task<string?> ObterUrlOnboardingAsync(string chaveApiSubconta, CancellationToken cancellationToken)
    {
        try
        {
            var documentos = await _api.EnviarAsync<ListaDocumentosAsaas>(HttpMethod.Get, "myAccount/documents", null, chaveApiSubconta, cancellationToken);
            return documentos.Data?.Select(d => d.OnboardingUrl).FirstOrDefault(u => !string.IsNullOrWhiteSpace(u));
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            _logger.LogWarning("[Asaas] link de envio de documentos indisponível: {Motivo}", ex.Message);
            return null;
        }
    }

    private sealed record ContaAsaas(string? Id, string? WalletId, string? ApiKey);

    private sealed record SituacaoContaAsaas(string? Id, string? General);

    private sealed record WebhookContaAsaas(string? Id, string? Event, SituacaoContaAsaas? AccountStatus);

    private sealed record ListaDocumentosAsaas(List<DocumentoAsaas>? Data);

    private sealed record DocumentoAsaas(string? Id, string? OnboardingUrl);

    private sealed record GarantiaAsaas(string? Id, string? Status, string? FinishDate);
}
