using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Viteme.Application.Interfaces;
using Viteme.Application.Services;
using Viteme.Domain.Enums;
using Viteme.Tests.Suporte;

namespace Viteme.Tests.Integracao;

/// <summary>
/// Fases 2 e 3 do Asaas com o gateway falso: conta de recebimento (E41/E42), trava do CNPJ, RN25, split na cobrança e
/// garantia (RN26). Desligadas, nada muda (os demais testes rodam assim).
/// </summary>
public class ContaRecebimentoTests(BancoFixture banco, ApiFactory api) : IClassFixture<BancoFixture>, IClassFixture<ApiFactory>
{
    private const string Rota = "/api/v1/usuarios/me/conta-recebimento";

    private static object CorpoConta() => new
    {
        tipoEmpresa = "LIMITED",
        telefone = "+5544999990000",
        faturamentoMensal = 5000m,
        cep = "87020-000",
        endereco = "Rua A",
        numero = "100",
        bairro = "Centro",
    };

    private static async Task<T> NoEscopoAsync<T>(AmbienteApi ambiente, Func<IServiceProvider, Task<T>> acao)
    {
        using var escopo = ambiente.Servicos.CreateScope();
        return await acao(escopo.ServiceProvider);
    }

    /// <summary>Organizador com conta de recebimento aprovada no "gateway".</summary>
    private static async Task<Sessao> OrganizadorAprovadoAsync(AmbienteApi ambiente)
    {
        var organizador = await ambiente.NovaSessaoOrganizadorAsync();
        (await ambiente.PostAsync(Rota, CorpoConta(), organizador.Token)).EnsureSuccessStatusCode();
        ambiente.Contas.SituacaoNoGateway = SituacaoContaRecebimento.Aprovada;
        (await ambiente.GetAsync(Rota, organizador.Token)).EnsureSuccessStatusCode();
        ambiente.Contas.SituacaoNoGateway = SituacaoContaRecebimento.Pendente;
        return organizador;
    }

    [FactBanco]
    public async Task Desligada_as_rotas_respondem_404()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoOrganizadorAsync();

        var criar = await ambiente.PostAsync(Rota, CorpoConta(), organizador.Token);
        var obter = await ambiente.GetAsync(Rota, organizador.Token);

        Assert.Equal(HttpStatusCode.NotFound, criar.StatusCode);
        Assert.Equal("RECEBIMENTOS_DESABILITADOS", (await AmbienteApi.CorpoAsync(criar)).GetProperty("codigo").GetString());
        Assert.Equal(HttpStatusCode.NotFound, obter.StatusCode);
        Assert.Empty(ambiente.Contas.Criadas);
    }

    [FactBanco]
    public async Task E41_abre_a_subconta_com_o_cnpj_do_perfil_e_guarda_so_a_chave_cifrada()
    {
        using var ambiente = new AmbienteApi(banco, api);
        ambiente.Contas.SubcontasHabilitadas = true;
        var organizador = await ambiente.NovaSessaoOrganizadorAsync();
        var perfil = await AmbienteApi.CorpoAsync(await ambiente.GetAsync("/api/v1/usuarios/me", organizador.Token));

        var resposta = await ambiente.PostAsync(Rota, CorpoConta(), organizador.Token);
        var corpo = await AmbienteApi.CorpoAsync(resposta);

        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        Assert.Equal("PENDENTE", corpo.GetProperty("situacao").GetString());
        Assert.StartsWith("https://onboarding/", corpo.GetProperty("urlOnboarding").GetString());
        var dados = Assert.Single(ambiente.Contas.Criadas);
        Assert.Equal(perfil.GetProperty("cnpj").GetString(), dados.Cnpj);
        Assert.Equal("87020000", dados.Cep);

        await using var contexto = banco.CriarContexto();
        var gravada = await contexto.ContasRecebimento.SingleAsync(c => c.UsuarioId == organizador.UsuarioId);
        var chave = Assert.Single(ambiente.Contas.ChavesEntregues);
        Assert.DoesNotContain(chave, gravada.ChaveApiCifrada);
        Assert.Equal(chave, await NoEscopoAsync(ambiente, sp => Task.FromResult(sp.GetRequiredService<ICifradorSegredos>().Decifrar(gravada.ChaveApiCifrada))));
    }

    [FactBanco]
    public async Task E41_exige_cnpj_verificado_e_so_uma_conta_por_organizador()
    {
        using var ambiente = new AmbienteApi(banco, api);
        ambiente.Contas.SubcontasHabilitadas = true;
        var semCnpj = await ambiente.NovaSessaoAsync();
        var organizador = await ambiente.NovaSessaoOrganizadorAsync();

        var proibido = await ambiente.PostAsync(Rota, CorpoConta(), semCnpj.Token);
        var primeira = await ambiente.PostAsync(Rota, CorpoConta(), organizador.Token);
        var segunda = await ambiente.PostAsync(Rota, CorpoConta(), organizador.Token);

        Assert.Equal(HttpStatusCode.Forbidden, proibido.StatusCode);
        Assert.Equal(HttpStatusCode.Created, primeira.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, segunda.StatusCode);
        Assert.Equal("CONTA_RECEBIMENTO_EXISTENTE", (await AmbienteApi.CorpoAsync(segunda)).GetProperty("codigo").GetString());
        Assert.Single(ambiente.Contas.Criadas);
    }

    [FactBanco]
    public async Task E41_com_gateway_recusando_responde_502_e_nada_e_gravado()
    {
        using var ambiente = new AmbienteApi(banco, api);
        ambiente.Contas.SubcontasHabilitadas = true;
        ambiente.Contas.FalharAoCriar = true;
        var organizador = await ambiente.NovaSessaoOrganizadorAsync();

        var resposta = await ambiente.PostAsync(Rota, CorpoConta(), organizador.Token);

        Assert.Equal(HttpStatusCode.BadGateway, resposta.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ambiente.GetAsync(Rota, organizador.Token)).StatusCode);
    }

    [FactBanco]
    public async Task E41_valida_os_dados_da_empresa()
    {
        using var ambiente = new AmbienteApi(banco, api);
        ambiente.Contas.SubcontasHabilitadas = true;
        var organizador = await ambiente.NovaSessaoOrganizadorAsync();

        var resposta = await ambiente.PostAsync(Rota, new { tipoEmpresa = "SA", telefone = "123", cep = "1" }, organizador.Token);

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.Empty(ambiente.Contas.Criadas);
    }

    [FactBanco]
    public async Task E42_atualiza_a_situacao_pendente_no_gateway_e_o_webhook_tambem()
    {
        using var ambiente = new AmbienteApi(banco, api);
        ambiente.Contas.SubcontasHabilitadas = true;
        var organizador = await ambiente.NovaSessaoOrganizadorAsync();
        (await ambiente.PostAsync(Rota, CorpoConta(), organizador.Token)).EnsureSuccessStatusCode();

        var pendente = await AmbienteApi.CorpoAsync(await ambiente.GetAsync(Rota, organizador.Token));
        ambiente.Contas.SituacaoNoGateway = SituacaoContaRecebimento.Aprovada;
        var aprovada = await AmbienteApi.CorpoAsync(await ambiente.GetAsync(Rota, organizador.Token));

        Assert.Equal("PENDENTE", pendente.GetProperty("situacao").GetString());
        Assert.Equal("APROVADA", aprovada.GetProperty("situacao").GetString());
        Assert.Equal(ambiente.Relogio.UtcAgora, aprovada.GetProperty("aprovadaEm").GetDateTime(), TimeSpan.FromSeconds(1));

        // Webhook de rejeição posterior (ex.: documento vencido): a situação segue o gateway.
        await using var contexto = banco.CriarContexto();
        var contaId = (await contexto.ContasRecebimento.SingleAsync(c => c.UsuarioId == organizador.UsuarioId)).ContaGatewayId;
        var processado = await NoEscopoAsync(ambiente, sp => sp.GetRequiredService<ContaRecebimentoService>()
            .ProcessarNotificacaoAsync(new NotificacaoConta(contaId, SituacaoContaRecebimento.Rejeitada)));
        var desconhecida = await NoEscopoAsync(ambiente, sp => sp.GetRequiredService<ContaRecebimentoService>()
            .ProcessarNotificacaoAsync(new NotificacaoConta("acc_outra", SituacaoContaRecebimento.Aprovada)));

        Assert.True(processado);
        Assert.False(desconhecida);
        ambiente.Contas.SituacaoNoGateway = SituacaoContaRecebimento.Rejeitada;
        Assert.Equal("REJEITADA", (await AmbienteApi.CorpoAsync(await ambiente.GetAsync(Rota, organizador.Token))).GetProperty("situacao").GetString());
    }

    [FactBanco]
    public async Task Cnpj_nao_pode_ser_trocado_depois_de_aberta_a_conta()
    {
        using var ambiente = new AmbienteApi(banco, api);
        ambiente.Contas.SubcontasHabilitadas = true;
        var organizador = await ambiente.NovaSessaoOrganizadorAsync();
        (await ambiente.PostAsync(Rota, CorpoConta(), organizador.Token)).EnsureSuccessStatusCode();

        var outroCnpj = Dados.NovoCnpj();
        var resposta = await ambiente.PutAsync("/api/v1/usuarios/me/cnpj", new { cnpj = outroCnpj }, organizador.Token);

        Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
        Assert.Equal("CNPJ_TRAVADO_POR_SUBCONTA", (await AmbienteApi.CorpoAsync(resposta)).GetProperty("codigo").GetString());
    }

    [FactBanco]
    public async Task RN25_evento_pago_so_e_publicado_com_a_conta_aprovada_e_o_gratuito_nao_muda()
    {
        using var ambiente = new AmbienteApi(banco, api);
        ambiente.Contas.SubcontasHabilitadas = true;
        var organizador = await ambiente.NovaSessaoOrganizadorAsync();
        var pago = await ambiente.CriarEventoAsync(organizador, ambiente.CorpoEvento(preco: 50m));
        var gratuito = await ambiente.CriarEventoAsync(organizador, ambiente.CorpoEvento());

        var semConta = await ambiente.PostAsync($"/api/v1/eventos/{pago}/publicar", token: organizador.Token);
        (await ambiente.PostAsync(Rota, CorpoConta(), organizador.Token)).EnsureSuccessStatusCode();
        var pendente = await ambiente.PostAsync($"/api/v1/eventos/{pago}/publicar", token: organizador.Token);
        var gratuitoPublicado = await ambiente.PostAsync($"/api/v1/eventos/{gratuito}/publicar", token: organizador.Token);
        ambiente.Contas.SituacaoNoGateway = SituacaoContaRecebimento.Aprovada;
        (await ambiente.GetAsync(Rota, organizador.Token)).EnsureSuccessStatusCode();
        var aprovada = await ambiente.PostAsync($"/api/v1/eventos/{pago}/publicar", token: organizador.Token);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, semConta.StatusCode);
        Assert.Equal("CONTA_RECEBIMENTO_PENDENTE", (await AmbienteApi.CorpoAsync(semConta)).GetProperty("codigo").GetString());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, pendente.StatusCode);
        Assert.Equal(HttpStatusCode.OK, gratuitoPublicado.StatusCode);
        Assert.Equal(HttpStatusCode.OK, aprovada.StatusCode);
    }

    [FactBanco]
    public async Task RN25_evento_gratuito_no_ar_nao_vira_pago_sem_conta_aprovada()
    {
        using var ambiente = new AmbienteApi(banco, api);
        ambiente.Contas.SubcontasHabilitadas = true;
        var organizador = await ambiente.NovaSessaoOrganizadorAsync();
        var evento = await ambiente.CriarEventoAsync(organizador, ambiente.CorpoEvento(), publicar: true);
        var (detalhe, etag) = await ambiente.DetalheAsync(evento, organizador.Token);

        var corpo = AmbienteApi.CorpoDeEdicao(detalhe, ("preco", 30m));
        corpo["motivoAlteracao"] = "Passou a ser pago";
        var resposta = await ambiente.PutAsync($"/api/v1/eventos/{evento}", corpo, organizador.Token, etag);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resposta.StatusCode);
        Assert.Equal("CONTA_RECEBIMENTO_PENDENTE", (await AmbienteApi.CorpoAsync(resposta)).GetProperty("codigo").GetString());
    }

    [FactBanco]
    public async Task Cobranca_vai_para_a_carteira_do_organizador_pelo_split()
    {
        using var ambiente = new AmbienteApi(banco, api);
        ambiente.Contas.SubcontasHabilitadas = true;
        var organizador = await OrganizadorAprovadoAsync(ambiente);
        var evento = await ambiente.CriarEventoAsync(organizador, ambiente.CorpoEvento(preco: 50m), publicar: true);
        var participante = await ambiente.NovaSessaoAsync();

        await ambiente.InscreverEmEventoPagoAsync(participante, evento);

        await using var contexto = banco.CriarContexto();
        var wallet = (await contexto.ContasRecebimento.SingleAsync(c => c.UsuarioId == organizador.UsuarioId)).WalletId;
        Assert.Equal(wallet, Assert.Single(ambiente.Gateway.Cobrancas).WalletRecebedor);
    }

    [FactBanco]
    public async Task Desligada_a_cobranca_fica_na_conta_raiz()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoOrganizadorAsync();
        var evento = await ambiente.CriarEventoAsync(organizador, ambiente.CorpoEvento(preco: 50m), publicar: true);

        await ambiente.InscreverEmEventoPagoAsync(await ambiente.NovaSessaoAsync(), evento);

        Assert.Null(Assert.Single(ambiente.Gateway.Cobrancas).WalletRecebedor);
    }

    [FactBanco]
    public async Task Conta_rejeitada_depois_de_publicar_impede_nova_cobranca_e_libera_a_vaga()
    {
        using var ambiente = new AmbienteApi(banco, api);
        ambiente.Contas.SubcontasHabilitadas = true;
        var organizador = await OrganizadorAprovadoAsync(ambiente);
        var evento = await ambiente.CriarEventoAsync(organizador, ambiente.CorpoEvento(preco: 50m, capacidade: 1), publicar: true);
        await using (var contexto = banco.CriarContexto())
        {
            var conta = await contexto.ContasRecebimento.SingleAsync(c => c.UsuarioId == organizador.UsuarioId);
            conta.AtualizarSituacao(SituacaoContaRecebimento.Rejeitada, ambiente.Relogio.UtcAgora);
            await contexto.SaveChangesAsync();
        }

        var resposta = await ambiente.InscreverAsync(await ambiente.NovaSessaoAsync(), evento);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resposta.StatusCode);
        Assert.Equal("CONTA_RECEBIMENTO_PENDENTE", (await AmbienteApi.CorpoAsync(resposta)).GetProperty("codigo").GetString());
        Assert.Empty(ambiente.Gateway.Cobrancas);
        await using var leitura = banco.CriarContexto();
        Assert.All(await leitura.Inscricoes.Where(i => i.EventoId == evento).ToListAsync(), i => Assert.Equal(StatusInscricao.Expirada, i.Status));
    }

    [FactBanco]
    public async Task RN26_garantia_e_registrada_na_aprovacao_e_liberada_dois_dias_depois_do_evento()
    {
        using var ambiente = new AmbienteApi(banco, api);
        ambiente.Contas.SubcontasHabilitadas = true;
        ambiente.Contas.GarantiaHabilitada = true;
        var organizador = await OrganizadorAprovadoAsync(ambiente);
        await NoEscopoAsync(ambiente, sp => sp.GetRequiredService<ContaRecebimentoService>().SincronizarAsync(10));
        Assert.Single(ambiente.Contas.GarantiasHabilitadas);

        ambiente.Relogio.Avancar(TimeSpan.FromMinutes(1));
        var evento = await ambiente.CriarEventoAsync(
            organizador, ambiente.CorpoEvento(preco: 50m, inicioEm: TimeSpan.FromDays(2), duracao: TimeSpan.FromHours(2)), publicar: true);
        var pago = await ambiente.InscreverEmEventoPagoAsync(await ambiente.NovaSessaoAsync(), evento);
        ambiente.Contas.Garantias[pago.IdTransacao] = "esc_1";
        (await ambiente.WebhookDeAsync(pago, "APROVADO")).EnsureSuccessStatusCode();

        // A cobrança nasceu na subconta, com a chave dela e sem split (o Asaas só retém cobranças da própria subconta).
        var cobranca = Assert.Single(ambiente.Gateway.Cobrancas);
        Assert.Null(cobranca.WalletRecebedor);
        Assert.Equal(Assert.Single(ambiente.Contas.ChavesEntregues), cobranca.ChaveApiRecebedor);
        await using (var contexto = banco.CriarContexto())
        {
            var gravado = await contexto.Pagamentos.SingleAsync(p => p.Id == pago.PagamentoId);
            Assert.Equal("esc_1", gravado.GarantiaId);
            Assert.Equal((await contexto.ContasRecebimento.SingleAsync(c => c.UsuarioId == organizador.UsuarioId)).Id, gravado.ContaRecebimentoId);
        }

        // Evento termina; um dia depois ainda não libera, dois dias depois libera uma única vez.
        ambiente.Relogio.Avancar(TimeSpan.FromDays(2) + TimeSpan.FromHours(3));
        await NoEscopoAsync(ambiente, sp => sp.GetRequiredService<EncerramentoEventosService>().EncerrarVencidosAsync());
        ambiente.Relogio.Avancar(TimeSpan.FromDays(1));
        var antes = await NoEscopoAsync(ambiente, sp => sp.GetRequiredService<GarantiaService>().LiberarVencidasAsync(10));
        ambiente.Relogio.Avancar(TimeSpan.FromDays(1) + TimeSpan.FromMinutes(1));
        var depois = await NoEscopoAsync(ambiente, sp => sp.GetRequiredService<GarantiaService>().LiberarVencidasAsync(10));
        var repetida = await NoEscopoAsync(ambiente, sp => sp.GetRequiredService<GarantiaService>().LiberarVencidasAsync(10));

        Assert.Equal(0, antes);
        Assert.Equal(1, depois);
        Assert.Equal(0, repetida);
        var encerrada = Assert.Single(ambiente.Contas.Encerradas);
        Assert.Equal((pago.IdTransacao, "esc_1"), (encerrada.IdTransacao, encerrada.GarantiaId));
        Assert.Contains(encerrada.Chave, ambiente.Contas.ChavesEntregues);
        await using var leitura = banco.CriarContexto();
        Assert.NotNull((await leitura.Pagamentos.SingleAsync(p => p.Id == pago.PagamentoId)).GarantiaLiberadaEm);
    }

    [FactBanco]
    public async Task Estorno_de_cobranca_criada_na_subconta_usa_a_chave_dela()
    {
        using var ambiente = new AmbienteApi(banco, api);
        ambiente.Contas.SubcontasHabilitadas = true;
        ambiente.Contas.GarantiaHabilitada = true;
        var organizador = await OrganizadorAprovadoAsync(ambiente);
        await NoEscopoAsync(ambiente, sp => sp.GetRequiredService<ContaRecebimentoService>().SincronizarAsync(10));
        var evento = await ambiente.CriarEventoAsync(organizador, ambiente.CorpoEvento(preco: 50m), publicar: true);
        var participante = await ambiente.NovaSessaoAsync();
        var pago = await ambiente.InscreverEmEventoPagoAsync(participante, evento);
        (await ambiente.WebhookDeAsync(pago, "APROVADO")).EnsureSuccessStatusCode();

        var resposta = await ambiente.PostAsync($"/api/v1/inscricoes/{pago.InscricaoId}/cancelar", token: participante.Token);

        resposta.EnsureSuccessStatusCode();
        Assert.Equal((pago.IdTransacao, 50m), Assert.Single(ambiente.Gateway.Estornos));
        Assert.Equal(Assert.Single(ambiente.Contas.ChavesEntregues), Assert.Single(ambiente.Gateway.ChavesDosEstornos));
    }

    [FactBanco]
    public async Task Sem_garantia_ligada_o_estorno_e_pela_conta_raiz()
    {
        using var ambiente = new AmbienteApi(banco, api);
        ambiente.Contas.SubcontasHabilitadas = true;
        var organizador = await OrganizadorAprovadoAsync(ambiente);
        var evento = await ambiente.CriarEventoAsync(organizador, ambiente.CorpoEvento(preco: 50m), publicar: true);
        var participante = await ambiente.NovaSessaoAsync();
        var pago = await ambiente.InscreverEmEventoPagoAsync(participante, evento);
        (await ambiente.WebhookDeAsync(pago, "APROVADO")).EnsureSuccessStatusCode();

        (await ambiente.PostAsync($"/api/v1/inscricoes/{pago.InscricaoId}/cancelar", token: participante.Token)).EnsureSuccessStatusCode();

        Assert.Null(Assert.Single(ambiente.Gateway.Cobrancas).ChaveApiRecebedor);
        Assert.Null(Assert.Single(ambiente.Gateway.ChavesDosEstornos));
    }

    [FactBanco]
    public async Task RN26_pagamento_sem_garantia_sai_da_fila_sem_chamar_o_gateway()
    {
        using var ambiente = new AmbienteApi(banco, api);
        ambiente.Contas.SubcontasHabilitadas = true;
        var organizador = await OrganizadorAprovadoAsync(ambiente);
        var evento = await ambiente.CriarEventoAsync(
            organizador, ambiente.CorpoEvento(preco: 50m, inicioEm: TimeSpan.FromDays(2), duracao: TimeSpan.FromHours(2)), publicar: true);
        var pago = await ambiente.InscreverEmEventoPagoAsync(await ambiente.NovaSessaoAsync(), evento);
        (await ambiente.WebhookDeAsync(pago, "APROVADO")).EnsureSuccessStatusCode();

        // A garantia só é ligada depois do pagamento: ele não ficou retido.
        ambiente.Contas.GarantiaHabilitada = true;
        ambiente.Relogio.Avancar(TimeSpan.FromDays(5));
        await NoEscopoAsync(ambiente, sp => sp.GetRequiredService<ContaRecebimentoService>().SincronizarAsync(10));
        await NoEscopoAsync(ambiente, sp => sp.GetRequiredService<EncerramentoEventosService>().EncerrarVencidosAsync());
        var liberadas = await NoEscopoAsync(ambiente, sp => sp.GetRequiredService<GarantiaService>().LiberarVencidasAsync(10));

        Assert.Equal(0, liberadas);
        Assert.Empty(ambiente.Contas.Encerradas);
        await using var leitura = banco.CriarContexto();
        var pagamento = await leitura.Pagamentos.SingleAsync(p => p.Id == pago.PagamentoId);
        Assert.Null(pagamento.GarantiaId);
        Assert.NotNull(pagamento.GarantiaLiberadaEm);
    }
}
