using Microsoft.Extensions.Logging;
using Viteme.Application.DTOs.Usuarios;
using Viteme.Application.Excecoes;
using Viteme.Application.Interfaces;
using Viteme.Domain.Entities;
using Viteme.Domain.Enums;
using Viteme.Domain.Excecoes;

namespace Viteme.Application.Services;

/// <summary>
/// Conta de recebimento do organizador (Fase 2 do Asaas, E41/E42): subconta no gateway que recebe o split das cobranças.
/// Desligada pela configuração, as rotas respondem 404 e nada aqui chama o gateway.
/// </summary>
public class ContaRecebimentoService(
    IContaRecebimentoRepository contas,
    IUsuarioRepository usuarios,
    IContasRecebimento gateway,
    ICifradorSegredos cifrador,
    IUnitOfWork unitOfWork,
    IRelogio relogio,
    ILogger<ContaRecebimentoService> logger)
{
    /// <summary>E41: abre a subconta com o CNPJ verificado do perfil (RN24). Uma por organizador.</summary>
    public async Task<ContaRecebimentoResponse> CriarAsync(
        Guid usuarioId, CriarContaRecebimentoRequest requisicao, CancellationToken cancellationToken = default)
    {
        GarantirHabilitado();
        var usuario = await usuarios.ObterPorIdAsync(usuarioId, cancellationToken)
            ?? throw new RegraDeNegocioException("USUARIO_NAO_ENCONTRADO", "Usuário não encontrado", "O usuário autenticado não existe mais.", 401);
        usuario.GarantirPodeOrganizar();

        if (await contas.ObterPorUsuarioAsync(usuarioId, cancellationToken) is not null)
        {
            throw ContaJaExiste();
        }

        SubcontaCriada criada;
        try
        {
            criada = await gateway.CriarSubcontaAsync(
                new DadosSubconta(
                    usuario.Id, usuario.Cnpj!, usuario.RazaoSocial ?? usuario.Nome,
                    string.IsNullOrWhiteSpace(requisicao.Email) ? usuario.Email : requisicao.Email.Trim(), requisicao.TipoEmpresa,
                    requisicao.Telefone, requisicao.FaturamentoMensal!.Value, new string(requisicao.Cep.Where(char.IsDigit).ToArray()),
                    requisicao.Endereco.Trim(), requisicao.Numero.Trim(), string.IsNullOrWhiteSpace(requisicao.Complemento) ? null : requisicao.Complemento.Trim(),
                    requisicao.Bairro.Trim()),
                cancellationToken);
        }
        catch (DadosRecusadosPeloGatewayException ex)
        {
            logger.LogWarning("Gateway recusou os dados da subconta do usuário {UsuarioId}: {Motivo}", usuarioId, ex.Message);
            throw new RegraDeNegocioException("CONTA_RECEBIMENTO_RECUSADA", "Dados recusados pelo meio de pagamento",
                $"O meio de pagamento recusou os dados: {ex.Message}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Gateway recusou ou não respondeu a criação da subconta do usuário {UsuarioId}.", usuarioId);
            throw new RegraDeNegocioException("GATEWAY_INDISPONIVEL", "Gateway indisponível",
                "Não foi possível abrir a conta de recebimento agora. Confira os dados e tente novamente.", 502);
        }

        var conta = new ContaRecebimento(usuarioId, criada.ContaId, criada.WalletId, cifrador.Cifrar(criada.ChaveApi), criada.UrlOnboarding);
        contas.Adicionar(conta);
        try
        {
            await unitOfWork.SalvarAsync(cancellationToken);
        }
        catch (ViolacaoDeUnicidadeException)
        {
            // Duas requisições simultâneas: a outra gravou primeiro. A subconta criada aqui fica órfã no gateway.
            logger.LogError("Subconta {ContaId} criada no gateway, mas o usuário {UsuarioId} já tinha conta gravada.", criada.ContaId, usuarioId);
            throw ContaJaExiste();
        }

        // A análise pode ser imediata (ex.: aprovação simulada no sandbox): já responde com a situação atual do gateway.
        if (await AtualizarDoGatewayAsync(conta, cancellationToken))
        {
            await unitOfWork.SalvarAsync(cancellationToken);
        }

        return Mapear(conta);
    }

    /// <summary>E42: situação da conta. Enquanto pendente, confere no gateway (melhor esforço) antes de responder.</summary>
    public async Task<ContaRecebimentoResponse> ObterAsync(Guid usuarioId, CancellationToken cancellationToken = default)
    {
        GarantirHabilitado();
        var conta = await contas.ObterPorUsuarioAsync(usuarioId, cancellationToken)
            ?? throw new RegraDeNegocioException("CONTA_RECEBIMENTO_NAO_ENCONTRADA", "Conta de recebimento não encontrada",
                "Você ainda não abriu a conta de recebimento.", 404);

        if (!conta.EstaAprovada && await AtualizarDoGatewayAsync(conta, cancellationToken))
        {
            await unitOfWork.SalvarAsync(cancellationToken);
        }

        return Mapear(conta);
    }

    /// <summary>Webhook de situação de conta, já autenticado pelo chamador. Conta desconhecida é ignorada (200 sem processar).</summary>
    public async Task<bool> ProcessarNotificacaoAsync(NotificacaoConta notificacao, CancellationToken cancellationToken = default)
    {
        var conta = await contas.ObterPorContaGatewayAsync(notificacao.ContaId, cancellationToken);
        if (conta is null || conta.Situacao == notificacao.Situacao)
        {
            return false;
        }

        conta.AtualizarSituacao(notificacao.Situacao, relogio.UtcAgora);
        await unitOfWork.SalvarAsync(cancellationToken);
        logger.LogInformation("Conta de recebimento {ContaId} agora {Situacao}.", conta.ContaGatewayId, conta.Situacao);
        return true;
    }

    /// <summary>
    /// Tarefa periódica: confere no gateway as contas pendentes (caso um webhook se perca) e, com a Fase 3 ligada, liga a
    /// garantia nas contas aprovadas que ainda não a têm. Retorna quantas contas mudaram.
    /// </summary>
    public async Task<int> SincronizarAsync(int limite, CancellationToken cancellationToken = default)
    {
        if (!gateway.SubcontasHabilitadas)
        {
            return 0;
        }

        var alteradas = 0;
        foreach (var conta in await contas.ListarPendentesAsync(limite, cancellationToken))
        {
            if (await AtualizarDoGatewayAsync(conta, cancellationToken))
            {
                alteradas++;
            }
        }

        if (gateway.GarantiaHabilitada)
        {
            foreach (var conta in await contas.ListarAprovadasSemGarantiaAsync(limite, cancellationToken))
            {
                try
                {
                    await gateway.HabilitarGarantiaAsync(conta.ContaGatewayId, cancellationToken);
                    conta.RegistrarGarantiaHabilitada(relogio.UtcAgora);
                    alteradas++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Falha ao ligar a garantia da subconta {ContaId}; tenta de novo no próximo ciclo.", conta.ContaGatewayId);
                }
            }
        }

        await unitOfWork.SalvarAsync(cancellationToken);
        return alteradas;
    }

    /// <summary>Carteira que recebe o split, com a conta aprovada (RN25). Lança 422 <c>CONTA_RECEBIMENTO_PENDENTE</c> se não houver.</summary>
    public async Task<ContaRecebimento> ObterAprovadaAsync(Guid organizadorId, CancellationToken cancellationToken = default)
    {
        var conta = await contas.ObterPorUsuarioAsync(organizadorId, cancellationToken);
        return conta is { EstaAprovada: true } ? conta : throw ContaPendente();
    }

    /// <summary>
    /// Chave de API (decifrada) da subconta em que a cobrança foi criada, para Pix, consulta e estorno (Fase 3). Nula para
    /// cobrança da conta raiz.
    /// </summary>
    public async Task<string?> ChaveDaCobrancaAsync(Guid? contaRecebimentoId, CancellationToken cancellationToken = default)
    {
        if (contaRecebimentoId is not { } id)
        {
            return null;
        }

        var conta = await contas.ObterPorIdAsync(id, cancellationToken)
            ?? throw new InvalidOperationException($"Conta de recebimento {id} do pagamento não existe.");
        return cifrador.Decifrar(conta.ChaveApiCifrada);
    }

    internal static RegraDeNegocioException ContaPendente() =>
        new("CONTA_RECEBIMENTO_PENDENTE", "Conta de recebimento pendente",
            "Eventos pagos exigem a conta de recebimento aprovada. Abra ou conclua a conta em Perfil > Recebimentos.");

    private async Task<bool> AtualizarDoGatewayAsync(ContaRecebimento conta, CancellationToken cancellationToken)
    {
        try
        {
            var situacao = await gateway.ConsultarSituacaoAsync(cifrador.Decifrar(conta.ChaveApiCifrada), cancellationToken);
            var mudou = situacao.Situacao != conta.Situacao || (situacao.UrlOnboarding is not null && situacao.UrlOnboarding != conta.UrlOnboarding);
            conta.AtualizarSituacao(situacao.Situacao, relogio.UtcAgora);
            if (situacao.UrlOnboarding is not null)
            {
                conta.AtualizarUrlOnboarding(situacao.UrlOnboarding);
            }

            return mudou;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Falha ao consultar a situação da subconta {ContaId}.", conta.ContaGatewayId);
            return false;
        }
    }

    private void GarantirHabilitado()
    {
        if (!gateway.SubcontasHabilitadas)
        {
            throw new RegraDeNegocioException("RECEBIMENTOS_DESABILITADOS", "Recebimentos desabilitados",
                "A conta de recebimento ainda não está disponível.", 404);
        }
    }

    private RegraDeNegocioException ContaJaExiste() =>
        new("CONTA_RECEBIMENTO_EXISTENTE", "Conta de recebimento já existe", "Você já abriu a conta de recebimento.", 409);

    private ContaRecebimentoResponse Mapear(ContaRecebimento c) =>
        new(c.Situacao, c.UrlOnboarding, c.CriadoEm, c.AprovadaEm, c.GarantiaHabilitadaEm is not null);
}
