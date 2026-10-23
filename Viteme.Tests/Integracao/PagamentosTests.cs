using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Viteme.Application.Services;
using Viteme.Domain.Enums;
using Viteme.Infrastructure.Persistencia;
using Viteme.Tests.Suporte;

namespace Viteme.Tests.Integracao;

/// <summary>E24 (evento pago), E29–E31, expiração de reservas e estornos. Casos CT31–CT38 e regras RN11, RN12, RN15, RN23.</summary>
public class PagamentosTests(BancoFixture banco, ApiFactory api) : IClassFixture<BancoFixture>, IClassFixture<ApiFactory>
{
    private sealed record Cenario(AmbienteApi Ambiente, Sessao Organizador, Sessao Participante, Guid Evento);

    private async Task<Cenario> EventoPagoAsync(AmbienteApi ambiente, decimal preco = 50m, int? capacidade = 5, TimeSpan? inicioEm = null)
    {
        var organizador = await ambiente.NovaSessaoAsync();
        var participante = await ambiente.NovaSessaoAsync();
        var evento = await ambiente.CriarEventoAsync(
            organizador, ambiente.CorpoEvento(preco: preco, capacidade: capacidade, inicioEm: inicioEm), publicar: true);
        return new Cenario(ambiente, organizador, participante, evento);
    }

    private async Task<T> NoBancoAsync<T>(Func<VitemeDbContext, Task<T>> consulta)
    {
        await using var contexto = banco.CriarContexto();
        return await consulta(contexto);
    }

    private async Task ExecutarAsync<T>(AmbienteApi ambiente, Func<T, Task> acao) where T : notnull
    {
        using var escopo = ambiente.Servicos.CreateScope();
        await acao(escopo.ServiceProvider.GetRequiredService<T>());
    }

    // ---- E24 (evento pago) ------------------------------------------------------------------------

    [FactBanco]
    public async Task CT31_inscricao_paga_fica_pendente_com_reserva_de_30_minutos_e_cobranca_do_valor_exato()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var c = await EventoPagoAsync(ambiente, preco: 50m);
        var antes = ambiente.Relogio.UtcAgora;

        var resposta = await ambiente.InscreverAsync(c.Participante, c.Evento);
        var corpo = await AmbienteApi.CorpoAsync(resposta);
        var pagamento = corpo.GetProperty("pagamento");

        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        Assert.Equal("PENDENTE_PAGAMENTO", corpo.GetProperty("status").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, corpo.GetProperty("codigoCheckin").ValueKind);
        Assert.Equal(antes.AddMinutes(30), corpo.GetProperty("reservadaAte").GetDateTime());
        Assert.Equal(50.00m, pagamento.GetProperty("valor").GetDecimal());
        Assert.StartsWith("https://", pagamento.GetProperty("urlPagamento").GetString());

        var cobranca = Assert.Single(ambiente.Gateway.Cobrancas);
        Assert.Equal(50.00m, cobranca.Valor);
        Assert.Equal(pagamento.GetProperty("id").GetGuid(), cobranca.ReferenciaExterna);
        Assert.Equal(antes.AddMinutes(30), cobranca.ValidaAte);
    }

    [FactBanco]
    public async Task Reserva_pendente_ocupa_vaga_ate_vencer()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var c = await EventoPagoAsync(ambiente, capacidade: 1);
        await ambiente.InscreverEmEventoPagoAsync(c.Participante, c.Evento);
        var outro = await ambiente.NovaSessaoAsync();

        var lotado = await ambiente.InscreverAsync(outro, c.Evento);
        ambiente.Relogio.Avancar(TimeSpan.FromMinutes(31));
        var liberado = await ambiente.InscreverAsync(outro, c.Evento);

        Assert.Equal("SEM_VAGAS", await AmbienteApi.CodigoErroAsync(lotado));
        Assert.Equal(HttpStatusCode.Created, liberado.StatusCode);
    }

    [FactBanco]
    public async Task CT38_gateway_fora_do_ar_retorna_502_expira_a_inscricao_e_libera_a_vaga()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var c = await EventoPagoAsync(ambiente, capacidade: 1);
        ambiente.Gateway.FalharAoCriar = true;

        var resposta = await ambiente.InscreverAsync(c.Participante, c.Evento);

        Assert.Equal(HttpStatusCode.BadGateway, resposta.StatusCode);
        Assert.Equal("GATEWAY_INDISPONIVEL", await AmbienteApi.CodigoErroAsync(resposta));

        var (inscricao, pagamento) = await NoBancoAsync(async db =>
        {
            var inscricaoDoEvento = await db.Inscricoes.SingleAsync(i => i.EventoId == c.Evento);
            return (inscricaoDoEvento, await db.Pagamentos.SingleAsync(p => p.InscricaoId == inscricaoDoEvento.Id));
        });
        Assert.Equal(StatusInscricao.Expirada, inscricao.Status);
        Assert.Equal(StatusPagamento.Expirado, pagamento.Status);

        ambiente.Gateway.FalharAoCriar = false;
        var outra = await ambiente.InscreverAsync(await ambiente.NovaSessaoAsync(), c.Evento);
        Assert.Equal(HttpStatusCode.Created, outra.StatusCode);
    }

    // ---- E31 webhook ------------------------------------------------------------------------------

    [FactBanco]
    public async Task CT32_webhook_aprovado_confirma_a_inscricao_e_notifica()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var c = await EventoPagoAsync(ambiente);
        var pago = await ambiente.InscreverEmEventoPagoAsync(c.Participante, c.Evento);

        var resposta = await ambiente.WebhookDeAsync(pago, "APROVADO");

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.True((await AmbienteApi.CorpoAsync(resposta)).GetProperty("processado").GetBoolean());
        Assert.Equal("APROVADO", await ambiente.StatusDoPagamentoAsync(c.Participante, pago.InscricaoId));

        var minhas = await AmbienteApi.CorpoAsync(await ambiente.GetAsync("/api/v1/inscricoes/minhas", c.Participante.Token));
        Assert.Equal("CONFIRMADA", minhas[0].GetProperty("status").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, minhas[0].GetProperty("reservadaAte").ValueKind);

        var notificacoes = await NoBancoAsync(db => db.Notificacoes.CountAsync(n =>
            n.UsuarioId == c.Participante.UsuarioId && n.Tipo == TipoNotificacao.Pagamento));
        Assert.Equal(1, notificacoes);
    }

    [FactBanco]
    public async Task CT33_webhook_repetido_e_processado_uma_unica_vez()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var c = await EventoPagoAsync(ambiente);
        var pago = await ambiente.InscreverEmEventoPagoAsync(c.Participante, c.Evento);

        var primeira = await ambiente.WebhookDeAsync(pago, "APROVADO");
        var segunda = await ambiente.WebhookDeAsync(pago, "APROVADO");

        Assert.Equal(HttpStatusCode.OK, primeira.StatusCode);
        Assert.Equal(HttpStatusCode.OK, segunda.StatusCode);
        Assert.True((await AmbienteApi.CorpoAsync(primeira)).GetProperty("processado").GetBoolean());
        Assert.False((await AmbienteApi.CorpoAsync(segunda)).GetProperty("processado").GetBoolean());
        Assert.Equal(1, await NoBancoAsync(db => db.Notificacoes.CountAsync(n =>
            n.UsuarioId == c.Participante.UsuarioId && n.Tipo == TipoNotificacao.Pagamento)));
        Assert.Empty(ambiente.Gateway.Estornos);
    }

    [FactBanco]
    public async Task Webhooks_identicos_simultaneos_sao_processados_uma_vez()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var c = await EventoPagoAsync(ambiente);
        var pago = await ambiente.InscreverEmEventoPagoAsync(c.Participante, c.Evento);

        var respostas = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => ambiente.WebhookDeAsync(pago, "APROVADO")));

        Assert.All(respostas, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        var processados = 0;
        foreach (var r in respostas)
        {
            processados += (await AmbienteApi.CorpoAsync(r)).GetProperty("processado").GetBoolean() ? 1 : 0;
        }

        Assert.Equal(1, processados);
        Assert.Equal(1, await NoBancoAsync(db => db.Notificacoes.CountAsync(n =>
            n.UsuarioId == c.Participante.UsuarioId && n.Tipo == TipoNotificacao.Pagamento)));
        Assert.Empty(ambiente.Gateway.Estornos);
    }

    [FactBanco]
    public async Task CT34_assinatura_invalida_ou_ausente_retorna_401_e_nada_muda()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var c = await EventoPagoAsync(ambiente);
        var pago = await ambiente.InscreverEmEventoPagoAsync(c.Participante, c.Evento);

        var errada = await ambiente.WebhookAsync(pago.IdTransacao, pago.PagamentoId, "APROVADO", pago.Valor, assinatura: "ABCDEF");
        var semCabecalho = await ambiente.Cliente.PostAsync("/api/v1/pagamentos/webhook",
            new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Unauthorized, errada.StatusCode);
        Assert.Equal("ASSINATURA_INVALIDA", await AmbienteApi.CodigoErroAsync(errada));
        Assert.Equal(HttpStatusCode.Unauthorized, semCabecalho.StatusCode);
        Assert.Equal("PENDENTE", await ambiente.StatusDoPagamentoAsync(c.Participante, pago.InscricaoId));
    }

    [FactBanco]
    public async Task Webhook_nao_passa_pelo_rate_limiting_nem_exige_jwt()
    {
        using var ambiente = new AmbienteApi(banco, api, limitacao: true);

        var respostas = await Task.WhenAll(Enumerable.Range(0, 25).Select(_ =>
            ambiente.WebhookAsync("x", Guid.NewGuid(), "APROVADO", 1m, assinatura: "ZZ")));

        Assert.All(respostas, r => Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode));
    }

    [FactBanco]
    public async Task Webhook_com_valor_divergente_transacao_desconhecida_ou_corpo_invalido()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var c = await EventoPagoAsync(ambiente);
        var pago = await ambiente.InscreverEmEventoPagoAsync(c.Participante, c.Evento);

        var divergente = await ambiente.WebhookAsync(pago.IdTransacao, pago.PagamentoId, "APROVADO", pago.Valor - 1m);
        var desconhecida = await ambiente.WebhookAsync("stub_nao_existe", Guid.NewGuid(), "APROVADO", pago.Valor);
        var outraTransacao = await ambiente.WebhookAsync("stub_outra", pago.PagamentoId, "APROVADO", pago.Valor);
        var naoEJson = await ambiente.WebhookAsync("", Guid.Empty, "", 0, corpoBruto: "isto não é json");
        var statusInvalido = await ambiente.WebhookAsync(pago.IdTransacao, pago.PagamentoId, "TALVEZ", pago.Valor);

        Assert.Equal((HttpStatusCode)422, divergente.StatusCode);
        Assert.Equal("VALOR_DIVERGENTE", await AmbienteApi.CodigoErroAsync(divergente));
        Assert.Equal(HttpStatusCode.NotFound, desconhecida.StatusCode);
        Assert.Equal("TRANSACAO_DESCONHECIDA", await AmbienteApi.CodigoErroAsync(desconhecida));
        Assert.Equal(HttpStatusCode.NotFound, outraTransacao.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, naoEJson.StatusCode);
        Assert.Equal("WEBHOOK_INVALIDO", await AmbienteApi.CodigoErroAsync(naoEJson));
        Assert.Equal(HttpStatusCode.BadRequest, statusInvalido.StatusCode);
        Assert.Equal("PENDENTE", await ambiente.StatusDoPagamentoAsync(c.Participante, pago.InscricaoId));
    }

    [FactBanco]
    public async Task Webhook_recusado_mantem_a_inscricao_pendente_e_permite_nova_cobranca()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var c = await EventoPagoAsync(ambiente);
        var pago = await ambiente.InscreverEmEventoPagoAsync(c.Participante, c.Evento);

        var recusa = await ambiente.WebhookDeAsync(pago, "RECUSADO");
        ambiente.Relogio.Avancar(TimeSpan.FromMinutes(1));
        var nova = await ambiente.PostAsync($"/api/v1/inscricoes/{pago.InscricaoId}/pagamento", token: c.Participante.Token);
        var corpo = await AmbienteApi.CorpoAsync(nova);

        Assert.Equal(HttpStatusCode.OK, recusa.StatusCode);
        Assert.Equal(HttpStatusCode.Created, nova.StatusCode);
        Assert.NotEqual(pago.PagamentoId, corpo.GetProperty("pagamentoId").GetGuid());
        Assert.Equal("PENDENTE", corpo.GetProperty("status").GetString());
        Assert.Equal(50m, corpo.GetProperty("valor").GetDecimal());
        Assert.Equal(2, ambiente.Gateway.Cobrancas.Count);
        Assert.Equal("PENDENTE", await ambiente.StatusDoPagamentoAsync(c.Participante, pago.InscricaoId));

        var inscricao = await NoBancoAsync(db => db.Inscricoes.SingleAsync(i => i.Id == pago.InscricaoId));
        Assert.Equal(StatusInscricao.PendentePagamento, inscricao.Status);
    }

    [FactBanco]
    public async Task Webhook_expirado_e_estornado_atualizam_o_pagamento()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var c = await EventoPagoAsync(ambiente);
        var expira = await ambiente.InscreverEmEventoPagoAsync(c.Participante, c.Evento);
        var outroParticipante = await ambiente.NovaSessaoAsync();
        var estorna = await ambiente.InscreverEmEventoPagoAsync(outroParticipante, c.Evento);
        await ambiente.WebhookDeAsync(estorna, "APROVADO");

        var expirado = await ambiente.WebhookDeAsync(expira, "EXPIRADO");
        var estornado = await ambiente.WebhookDeAsync(estorna, "ESTORNADO");

        Assert.Equal(HttpStatusCode.OK, expirado.StatusCode);
        Assert.Equal(HttpStatusCode.OK, estornado.StatusCode);
        Assert.Equal("EXPIRADO", await ambiente.StatusDoPagamentoAsync(c.Participante, expira.InscricaoId));
        Assert.Equal("ESTORNADO", await ambiente.StatusDoPagamentoAsync(outroParticipante, estorna.InscricaoId));
    }

    // ---- RN11: expiração de reservas --------------------------------------------------------------

    [FactBanco]
    public async Task CT35_reserva_sem_pagamento_expira_apos_30_minutos_e_libera_a_vaga()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var c = await EventoPagoAsync(ambiente, capacidade: 3);
        var pago = await ambiente.InscreverEmEventoPagoAsync(c.Participante, c.Evento);

        ambiente.Relogio.Avancar(TimeSpan.FromMinutes(29));
        int antesDoPrazo = 0, depoisDoPrazo = 0, repetida = 0;
        await ExecutarAsync<ExpiracaoReservasService>(ambiente, async s => antesDoPrazo = await s.ExpirarVencidasAsync());
        ambiente.Relogio.Avancar(TimeSpan.FromMinutes(2));
        await ExecutarAsync<ExpiracaoReservasService>(ambiente, async s => depoisDoPrazo = await s.ExpirarVencidasAsync());
        await ExecutarAsync<ExpiracaoReservasService>(ambiente, async s => repetida = await s.ExpirarVencidasAsync());

        Assert.Equal(0, antesDoPrazo);
        Assert.True(depoisDoPrazo >= 1);
        Assert.Equal(0, repetida);

        var (inscricao, pagamento) = await NoBancoAsync(async db => (
            await db.Inscricoes.SingleAsync(i => i.Id == pago.InscricaoId),
            await db.Pagamentos.SingleAsync(p => p.Id == pago.PagamentoId)));
        Assert.Equal(StatusInscricao.Expirada, inscricao.Status);
        Assert.Equal(StatusPagamento.Expirado, pagamento.Status);
        Assert.Equal(3, (await ambiente.DetalheAsync(c.Evento)).Corpo.GetProperty("vagasRestantes").GetInt32());
    }

    [FactBanco]
    public async Task Reserva_confirmada_pelo_pagamento_nao_expira()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var c = await EventoPagoAsync(ambiente);
        var pago = await ambiente.InscreverEmEventoPagoAsync(c.Participante, c.Evento);
        await ambiente.WebhookDeAsync(pago, "APROVADO");

        ambiente.Relogio.Avancar(TimeSpan.FromHours(2));
        await ExecutarAsync<ExpiracaoReservasService>(ambiente, s => s.ExpirarVencidasAsync());

        var inscricao = await NoBancoAsync(db => db.Inscricoes.SingleAsync(i => i.Id == pago.InscricaoId));
        Assert.Equal(StatusInscricao.Confirmada, inscricao.Status);
    }

    // ---- RN23: aprovação tardia e estornos --------------------------------------------------------

    [FactBanco]
    public async Task CT36_aprovacao_apos_a_expiracao_gera_estorno_automatico_e_nao_confirma()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var c = await EventoPagoAsync(ambiente);
        var pago = await ambiente.InscreverEmEventoPagoAsync(c.Participante, c.Evento);
        ambiente.Relogio.Avancar(TimeSpan.FromMinutes(31));
        await ExecutarAsync<ExpiracaoReservasService>(ambiente, s => s.ExpirarVencidasAsync());

        var resposta = await ambiente.WebhookDeAsync(pago, "APROVADO");

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var estorno = Assert.Single(ambiente.Gateway.Estornos);
        Assert.Equal(pago.IdTransacao, estorno.IdTransacao);
        Assert.Equal(50m, estorno.Valor);
        Assert.Equal("ESTORNADO", await ambiente.StatusDoPagamentoAsync(c.Participante, pago.InscricaoId));
        var inscricao = await NoBancoAsync(db => db.Inscricoes.SingleAsync(i => i.Id == pago.InscricaoId));
        Assert.Equal(StatusInscricao.Expirada, inscricao.Status);
    }

    [FactBanco]
    public async Task Aprovacao_com_reserva_vencida_antes_da_tarefa_tambem_estorna()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var c = await EventoPagoAsync(ambiente);
        var pago = await ambiente.InscreverEmEventoPagoAsync(c.Participante, c.Evento);

        ambiente.Relogio.Avancar(TimeSpan.FromMinutes(31)); // a tarefa ainda não rodou
        var resposta = await ambiente.WebhookDeAsync(pago, "APROVADO");

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Single(ambiente.Gateway.Estornos);
        var inscricao = await NoBancoAsync(db => db.Inscricoes.SingleAsync(i => i.Id == pago.InscricaoId));
        Assert.Equal(StatusInscricao.Expirada, inscricao.Status);
    }

    [FactBanco]
    public async Task Aprovacao_de_pagamento_de_inscricao_cancelada_gera_estorno()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var c = await EventoPagoAsync(ambiente, inicioEm: TimeSpan.FromDays(5));
        var pago = await ambiente.InscreverEmEventoPagoAsync(c.Participante, c.Evento);
        await ambiente.PostAsync($"/api/v1/inscricoes/{pago.InscricaoId}/cancelar", token: c.Participante.Token);

        var resposta = await ambiente.WebhookDeAsync(pago, "APROVADO");

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Single(ambiente.Gateway.Estornos);
        Assert.Equal("ESTORNADO", await ambiente.StatusDoPagamentoAsync(c.Participante, pago.InscricaoId));
    }

    [FactBanco]
    public async Task Falha_no_estorno_mantem_o_pagamento_aprovado_e_a_tarefa_tenta_de_novo()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var c = await EventoPagoAsync(ambiente);
        var pago = await ambiente.InscreverEmEventoPagoAsync(c.Participante, c.Evento);
        ambiente.Relogio.Avancar(TimeSpan.FromMinutes(31));
        ambiente.Gateway.FalharEstorno = true;

        var resposta = await ambiente.WebhookDeAsync(pago, "APROVADO");
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Equal("APROVADO", await ambiente.StatusDoPagamentoAsync(c.Participante, pago.InscricaoId));

        int comFalha = 0, apos = 0, repetida = 0;
        await ExecutarAsync<EstornoService>(ambiente, async s => comFalha = await s.ProcessarPendentesAsync());
        ambiente.Gateway.FalharEstorno = false;
        await ExecutarAsync<EstornoService>(ambiente, async s => apos = await s.ProcessarPendentesAsync());
        await ExecutarAsync<EstornoService>(ambiente, async s => repetida = await s.ProcessarPendentesAsync());

        Assert.Equal(0, comFalha);
        Assert.True(apos >= 1);
        Assert.Equal(0, repetida);
        Assert.Single(ambiente.Gateway.Estornos);
        Assert.Equal("ESTORNADO", await ambiente.StatusDoPagamentoAsync(c.Participante, pago.InscricaoId));
    }

    [FactBanco]
    public async Task Webhook_repetido_apos_falha_de_estorno_refaz_o_estorno()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var c = await EventoPagoAsync(ambiente);
        var pago = await ambiente.InscreverEmEventoPagoAsync(c.Participante, c.Evento);
        ambiente.Relogio.Avancar(TimeSpan.FromMinutes(31));
        ambiente.Gateway.FalharEstorno = true;
        await ambiente.WebhookDeAsync(pago, "APROVADO");

        ambiente.Gateway.FalharEstorno = false;
        var reenvio = await ambiente.WebhookDeAsync(pago, "APROVADO");

        Assert.Equal(HttpStatusCode.OK, reenvio.StatusCode);
        Assert.Single(ambiente.Gateway.Estornos);
        Assert.Equal("ESTORNADO", await ambiente.StatusDoPagamentoAsync(c.Participante, pago.InscricaoId));
    }

    [FactBanco]
    public async Task Segundo_pagamento_aprovado_para_inscricao_ja_confirmada_e_estornado()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var c = await EventoPagoAsync(ambiente);
        var pago = await ambiente.InscreverEmEventoPagoAsync(c.Participante, c.Evento);
        await ambiente.PostAsync($"/api/v1/inscricoes/{pago.InscricaoId}/pagamento", token: c.Participante.Token);
        await ambiente.WebhookDeAsync(pago, "RECUSADO");
        ambiente.Relogio.Avancar(TimeSpan.FromMinutes(1));
        var novo = await ambiente.PostAsync($"/api/v1/inscricoes/{pago.InscricaoId}/pagamento", token: c.Participante.Token);
        var idNovo = (await AmbienteApi.CorpoAsync(novo)).GetProperty("pagamentoId").GetGuid();
        var primeiroPago = await ambiente.WebhookDeAsync(pago, "APROVADO"); // o gateway aprovou a cobrança antiga (recusada antes)

        var segundoPago = await ambiente.WebhookAsync($"stub_{idNovo:N}", idNovo, "APROVADO", 50m);

        Assert.Equal(HttpStatusCode.OK, primeiroPago.StatusCode);
        Assert.Equal(HttpStatusCode.OK, segundoPago.StatusCode);
        var inscricao = await NoBancoAsync(db => db.Inscricoes.SingleAsync(i => i.Id == pago.InscricaoId));
        Assert.Equal(StatusInscricao.Confirmada, inscricao.Status);
        var estorno = Assert.Single(ambiente.Gateway.Estornos);
        Assert.Equal($"stub_{idNovo:N}", estorno.IdTransacao);
    }

    [FactBanco]
    public async Task CT37_cancelar_inscricao_paga_dentro_do_prazo_solicita_estorno_integral()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var c = await EventoPagoAsync(ambiente, inicioEm: TimeSpan.FromDays(5));
        var pago = await ambiente.InscreverEmEventoPagoAsync(c.Participante, c.Evento);
        await ambiente.WebhookDeAsync(pago, "APROVADO");

        var resposta = await ambiente.PostAsync($"/api/v1/inscricoes/{pago.InscricaoId}/cancelar", token: c.Participante.Token);
        var corpo = await AmbienteApi.CorpoAsync(resposta);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Equal("CANCELADA", corpo.GetProperty("status").GetString());
        Assert.True(corpo.GetProperty("estornoSolicitado").GetBoolean());
        var estorno = Assert.Single(ambiente.Gateway.Estornos);
        Assert.Equal(pago.IdTransacao, estorno.IdTransacao);
        Assert.Equal(50m, estorno.Valor);
        Assert.Equal("ESTORNADO", await ambiente.StatusDoPagamentoAsync(c.Participante, pago.InscricaoId));
    }

    [FactBanco]
    public async Task Cancelar_inscricao_com_cobranca_pendente_expira_a_cobranca_sem_estorno()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var c = await EventoPagoAsync(ambiente, inicioEm: TimeSpan.FromDays(5));
        var pago = await ambiente.InscreverEmEventoPagoAsync(c.Participante, c.Evento);

        var resposta = await ambiente.PostAsync($"/api/v1/inscricoes/{pago.InscricaoId}/cancelar", token: c.Participante.Token);

        Assert.False((await AmbienteApi.CorpoAsync(resposta)).GetProperty("estornoSolicitado").GetBoolean());
        Assert.Empty(ambiente.Gateway.Estornos);
        Assert.Equal("EXPIRADO", await ambiente.StatusDoPagamentoAsync(c.Participante, pago.InscricaoId));
    }

    [FactBanco]
    public async Task CT45_cancelar_o_evento_cancela_inscricoes_estorna_pagamentos_aprovados_e_notifica()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var c = await EventoPagoAsync(ambiente);
        var pagante = await ambiente.InscreverEmEventoPagoAsync(c.Participante, c.Evento);
        await ambiente.WebhookDeAsync(pagante, "APROVADO");
        var pendente = await ambiente.InscreverEmEventoPagoAsync(await ambiente.NovaSessaoAsync(), c.Evento);

        var resposta = await ambiente.PostAsync($"/api/v1/eventos/{c.Evento}/cancelar",
            new { motivo = "Cancelado por motivo de força maior" }, c.Organizador.Token);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var estorno = Assert.Single(ambiente.Gateway.Estornos);
        Assert.Equal(pagante.IdTransacao, estorno.IdTransacao);
        Assert.Equal("ESTORNADO", await ambiente.StatusDoPagamentoAsync(c.Participante, pagante.InscricaoId));

        var (inscricoes, pagamentoPendente, avisos) = await NoBancoAsync(async db => (
            await db.Inscricoes.Where(i => i.EventoId == c.Evento).ToListAsync(),
            await db.Pagamentos.SingleAsync(p => p.Id == pendente.PagamentoId),
            await db.Notificacoes.Where(n => n.EventoId == c.Evento).ToListAsync()));
        Assert.All(inscricoes, i => Assert.Equal(StatusInscricao.Cancelada, i.Status));
        Assert.Equal(StatusPagamento.Expirado, pagamentoPendente.Status);
        Assert.Contains(avisos, n => n.UsuarioId == c.Participante.UsuarioId && n.Tipo == TipoNotificacao.Cancelamento);
        Assert.Contains(avisos, n => n.UsuarioId == c.Participante.UsuarioId && n.Tipo == TipoNotificacao.Pagamento);
        Assert.DoesNotContain(avisos, n => n.UsuarioId == c.Organizador.UsuarioId);
    }

    [FactBanco]
    public async Task Cancelar_o_evento_com_gateway_fora_do_ar_deixa_o_estorno_pendente_para_a_tarefa()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var c = await EventoPagoAsync(ambiente);
        var pagante = await ambiente.InscreverEmEventoPagoAsync(c.Participante, c.Evento);
        await ambiente.WebhookDeAsync(pagante, "APROVADO");
        ambiente.Gateway.FalharEstorno = true;

        var resposta = await ambiente.PostAsync($"/api/v1/eventos/{c.Evento}/cancelar",
            new { motivo = "Cancelado por motivo de força maior" }, c.Organizador.Token);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Equal("APROVADO", await ambiente.StatusDoPagamentoAsync(c.Participante, pagante.InscricaoId));

        ambiente.Gateway.FalharEstorno = false;
        await ExecutarAsync<EstornoService>(ambiente, s => s.ProcessarPendentesAsync());

        Assert.Single(ambiente.Gateway.Estornos);
        Assert.Equal("ESTORNADO", await ambiente.StatusDoPagamentoAsync(c.Participante, pagante.InscricaoId));
    }

    // ---- E29 e E30 --------------------------------------------------------------------------------

    [FactBanco]
    public async Task Gerar_cobranca_com_cobranca_pendente_devolve_a_mesma_sem_chamar_o_gateway()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var c = await EventoPagoAsync(ambiente);
        var pago = await ambiente.InscreverEmEventoPagoAsync(c.Participante, c.Evento);

        var resposta = await ambiente.PostAsync($"/api/v1/inscricoes/{pago.InscricaoId}/pagamento", token: c.Participante.Token);
        var corpo = await AmbienteApi.CorpoAsync(resposta);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Equal(pago.PagamentoId, corpo.GetProperty("pagamentoId").GetGuid());
        Assert.Single(ambiente.Gateway.Cobrancas);
    }

    [FactBanco]
    public async Task Gerar_cobranca_valida_dono_aprovacao_evento_gratuito_e_reserva()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var c = await EventoPagoAsync(ambiente);
        var pago = await ambiente.InscreverEmEventoPagoAsync(c.Participante, c.Evento);
        var gratuito = await ambiente.CriarEventoAsync(c.Organizador, publicar: true);
        var inscricaoGratuita = await ambiente.InscreverComSucessoAsync(c.Participante, gratuito);
        var estranho = await ambiente.NovaSessaoAsync();

        var anonima = await ambiente.PostAsync($"/api/v1/inscricoes/{pago.InscricaoId}/pagamento");
        var alheia = await ambiente.PostAsync($"/api/v1/inscricoes/{pago.InscricaoId}/pagamento", token: estranho.Token);
        var eventoGratuito = await ambiente.PostAsync($"/api/v1/inscricoes/{inscricaoGratuita}/pagamento", token: c.Participante.Token);
        await ambiente.WebhookDeAsync(pago, "APROVADO");
        var jaAprovado = await ambiente.PostAsync($"/api/v1/inscricoes/{pago.InscricaoId}/pagamento", token: c.Participante.Token);

        Assert.Equal(HttpStatusCode.Unauthorized, anonima.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, alheia.StatusCode);
        Assert.Equal((HttpStatusCode)422, eventoGratuito.StatusCode);
        Assert.Equal("EVENTO_GRATUITO", await AmbienteApi.CodigoErroAsync(eventoGratuito));
        Assert.Equal(HttpStatusCode.Conflict, jaAprovado.StatusCode);
        Assert.Equal("PAGAMENTO_JA_APROVADO", await AmbienteApi.CodigoErroAsync(jaAprovado));
    }

    [FactBanco]
    public async Task Gerar_cobranca_apos_reserva_vencida_retorna_422()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var c = await EventoPagoAsync(ambiente);
        var pago = await ambiente.InscreverEmEventoPagoAsync(c.Participante, c.Evento);

        ambiente.Relogio.Avancar(TimeSpan.FromMinutes(31));
        var resposta = await ambiente.PostAsync($"/api/v1/inscricoes/{pago.InscricaoId}/pagamento", token: c.Participante.Token);

        Assert.Equal((HttpStatusCode)422, resposta.StatusCode);
        Assert.Equal("RESERVA_EXPIRADA", await AmbienteApi.CodigoErroAsync(resposta));
        var inscricao = await NoBancoAsync(db => db.Inscricoes.SingleAsync(i => i.Id == pago.InscricaoId));
        Assert.Equal(StatusInscricao.Expirada, inscricao.Status);
    }

    [FactBanco]
    public async Task Gateway_fora_do_ar_ao_gerar_nova_cobranca_retorna_502_e_mantem_a_reserva()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var c = await EventoPagoAsync(ambiente);
        var pago = await ambiente.InscreverEmEventoPagoAsync(c.Participante, c.Evento);
        await ambiente.WebhookDeAsync(pago, "RECUSADO");
        ambiente.Relogio.Avancar(TimeSpan.FromMinutes(1));
        ambiente.Gateway.FalharAoCriar = true;

        var resposta = await ambiente.PostAsync($"/api/v1/inscricoes/{pago.InscricaoId}/pagamento", token: c.Participante.Token);

        Assert.Equal(HttpStatusCode.BadGateway, resposta.StatusCode);
        var inscricao = await NoBancoAsync(db => db.Inscricoes.SingleAsync(i => i.Id == pago.InscricaoId));
        Assert.Equal(StatusInscricao.PendentePagamento, inscricao.Status);

        ambiente.Gateway.FalharAoCriar = false;
        ambiente.Relogio.Avancar(TimeSpan.FromMinutes(1));
        var tentativa = await ambiente.PostAsync($"/api/v1/inscricoes/{pago.InscricaoId}/pagamento", token: c.Participante.Token);
        Assert.Equal(HttpStatusCode.Created, tentativa.StatusCode);
    }

    [FactBanco]
    public async Task Status_do_pagamento_so_para_o_dono_e_404_sem_cobranca()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var c = await EventoPagoAsync(ambiente);
        var pago = await ambiente.InscreverEmEventoPagoAsync(c.Participante, c.Evento);
        var gratuito = await ambiente.CriarEventoAsync(c.Organizador, publicar: true);
        var inscricaoGratuita = await ambiente.InscreverComSucessoAsync(c.Participante, gratuito);
        var estranho = await ambiente.NovaSessaoAsync();

        var doDono = await ambiente.GetAsync($"/api/v1/inscricoes/{pago.InscricaoId}/pagamento", c.Participante.Token);
        var corpo = await AmbienteApi.CorpoAsync(doDono);
        var alheio = await ambiente.GetAsync($"/api/v1/inscricoes/{pago.InscricaoId}/pagamento", estranho.Token);
        var anonimo = await ambiente.GetAsync($"/api/v1/inscricoes/{pago.InscricaoId}/pagamento");
        var semCobranca = await ambiente.GetAsync($"/api/v1/inscricoes/{inscricaoGratuita}/pagamento", c.Participante.Token);

        Assert.Equal(HttpStatusCode.OK, doDono.StatusCode);
        Assert.Equal("PENDENTE", corpo.GetProperty("status").GetString());
        Assert.Equal("PENDENTE_PAGAMENTO", corpo.GetProperty("statusInscricao").GetString());
        Assert.Equal(50m, corpo.GetProperty("valor").GetDecimal());
        Assert.Equal(HttpStatusCode.NotFound, alheio.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonimo.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, semCobranca.StatusCode);
        Assert.Equal("PAGAMENTO_NAO_ENCONTRADO", await AmbienteApi.CodigoErroAsync(semCobranca));
    }

    // ---- Simulador (demonstração) -----------------------------------------------------------------

    [FactBanco]
    public async Task Simulador_aprova_o_pagamento_do_proprio_usuario_pelo_mesmo_caminho_do_webhook()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var c = await EventoPagoAsync(ambiente);
        var pago = await ambiente.InscreverEmEventoPagoAsync(c.Participante, c.Evento);
        var estranho = await ambiente.NovaSessaoAsync();

        var alheio = await ambiente.PostAsync($"/api/v1/simulador/pagamentos/{pago.IdTransacao}/aprovar", token: estranho.Token);
        var anonimo = await ambiente.PostAsync($"/api/v1/simulador/pagamentos/{pago.IdTransacao}/aprovar");
        var resposta = await ambiente.PostAsync($"/api/v1/simulador/pagamentos/{pago.IdTransacao}/aprovar", token: c.Participante.Token);

        Assert.Equal(HttpStatusCode.NotFound, alheio.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonimo.StatusCode);
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Equal("APROVADO", await ambiente.StatusDoPagamentoAsync(c.Participante, pago.InscricaoId));
    }
}
