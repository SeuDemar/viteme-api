using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Viteme.Application.Services;
using Viteme.Domain.Enums;
using Viteme.Tests.Suporte;

namespace Viteme.Tests.Integracao;

/// <summary>E45 (CPF do pagador), regra "CPF obrigatório para pagar" e reconciliação com o gateway (P29).</summary>
public class CpfEReconciliacaoTests(BancoFixture banco, ApiFactory api) : IClassFixture<BancoFixture>, IClassFixture<ApiFactory>
{
    private const string CpfValido = "123.456.789-09";

    private async Task<(Sessao Participante, Guid Evento)> EventoPagoAsync(AmbienteApi ambiente)
    {
        var organizador = await ambiente.NovaSessaoAsync();
        var participante = await ambiente.NovaSessaoAsync();
        var evento = await ambiente.CriarEventoAsync(organizador, ambiente.CorpoEvento(preco: 50m, capacidade: 5), publicar: true);
        return (participante, evento);
    }

    [FactBanco]
    public async Task Sem_cpf_a_inscricao_paga_e_recusada_antes_de_reservar_a_vaga()
    {
        using var ambiente = new AmbienteApi(banco, api);
        ambiente.Gateway.ExigirDocumento = true;
        var (participante, evento) = await EventoPagoAsync(ambiente);

        var resposta = await ambiente.InscreverAsync(participante, evento);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resposta.StatusCode);
        Assert.Equal("CPF_OBRIGATORIO", await AmbienteApi.CodigoErroAsync(resposta));
        Assert.Empty(ambiente.Gateway.Cobrancas);
        await using var contexto = banco.CriarContexto();
        Assert.False(await contexto.Inscricoes.AnyAsync(i => i.EventoId == evento));
    }

    [FactBanco]
    public async Task Com_cpf_informado_a_inscricao_paga_segue_e_o_gateway_recebe_o_documento()
    {
        using var ambiente = new AmbienteApi(banco, api);
        ambiente.Gateway.ExigirDocumento = true;
        var (participante, evento) = await EventoPagoAsync(ambiente);

        var perfil = await ambiente.PutAsync("/api/v1/usuarios/me/cpf", new { cpf = CpfValido }, participante.Token);
        var corpo = await AmbienteApi.CorpoAsync(perfil);
        var inscricao = await ambiente.InscreverAsync(participante, evento);

        Assert.Equal(HttpStatusCode.OK, perfil.StatusCode);
        Assert.True(corpo.GetProperty("temCpf").GetBoolean());
        Assert.Equal("***.***.***-09", corpo.GetProperty("cpfMascarado").GetString());
        Assert.False(corpo.TryGetProperty("cpf", out _));
        Assert.Equal(HttpStatusCode.Created, inscricao.StatusCode);
        Assert.Equal("12345678909", Assert.Single(ambiente.Gateway.Cobrancas).Pagador!.CpfCnpj);
    }

    [FactBanco]
    public async Task Cpf_invalido_retorna_400_e_nada_e_gravado()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var sessao = await ambiente.NovaSessaoAsync();

        var resposta = await ambiente.PutAsync("/api/v1/usuarios/me/cpf", new { cpf = "111.111.111-11" }, sessao.Token);

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        await using var contexto = banco.CriarContexto();
        Assert.Null((await contexto.Usuarios.SingleAsync(u => u.Id == sessao.UsuarioId)).Cpf);
    }

    [FactBanco]
    public async Task Gateway_que_nao_exige_documento_nao_pede_cpf()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var (participante, evento) = await EventoPagoAsync(ambiente);

        Assert.Equal(HttpStatusCode.Created, (await ambiente.InscreverAsync(participante, evento)).StatusCode);
    }

    [FactBanco]
    public async Task E46_devolve_o_pix_da_cobranca_pendente_e_404_quando_o_gateway_nao_oferece_o_qr()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var (participante, evento) = await EventoPagoAsync(ambiente);
        var corpo = await AmbienteApi.CorpoAsync(await ambiente.InscreverAsync(participante, evento));
        var rota = $"/api/v1/inscricoes/{corpo.GetProperty("id").GetGuid()}/pagamento/pix";

        var indisponivel = await ambiente.GetAsync(rota, participante.Token);
        ambiente.Gateway.Pix = new Viteme.Application.Interfaces.PixCobranca("000201pix", "iVBOR", null);
        var disponivel = await ambiente.GetAsync(rota, participante.Token);
        var outro = await ambiente.GetAsync(rota, (await ambiente.NovaSessaoAsync()).Token);

        Assert.Equal(HttpStatusCode.NotFound, indisponivel.StatusCode);
        Assert.Equal("PIX_INDISPONIVEL", await AmbienteApi.CodigoErroAsync(indisponivel));
        Assert.Equal(HttpStatusCode.OK, disponivel.StatusCode);
        Assert.Equal("000201pix", (await AmbienteApi.CorpoAsync(disponivel)).GetProperty("copiaECola").GetString());
        Assert.Equal(HttpStatusCode.NotFound, outro.StatusCode); // inscrição de outro usuário
    }

    [FactBanco]
    public async Task Reconciliacao_confirma_a_inscricao_quando_o_webhook_se_perdeu()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var (participante, evento) = await EventoPagoAsync(ambiente);
        var corpo = await AmbienteApi.CorpoAsync(await ambiente.InscreverAsync(participante, evento));
        var pagamentoId = corpo.GetProperty("pagamento").GetProperty("id").GetGuid();
        ambiente.Gateway.StatusNoGateway[$"stub_{pagamentoId:N}"] = StatusPagamento.Aprovado;

        ambiente.Relogio.Avancar(TimeSpan.FromMinutes(5));
        using var escopo = ambiente.Servicos.CreateScope();
        var corrigidos = await escopo.ServiceProvider.GetRequiredService<PagamentoService>().ReconciliarPendentesAsync(50);

        Assert.Equal(1, corrigidos);
        await using var contexto = banco.CriarContexto();
        Assert.Equal(StatusPagamento.Aprovado, (await contexto.Pagamentos.SingleAsync(p => p.Id == pagamentoId)).Status);
        Assert.Equal(StatusInscricao.Confirmada, (await contexto.Inscricoes.SingleAsync(i => i.Id == corpo.GetProperty("id").GetGuid())).Status);
    }

    [FactBanco]
    public async Task Reconciliacao_com_reserva_expirada_aprova_o_pagamento_e_manda_estornar()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var (participante, evento) = await EventoPagoAsync(ambiente);
        var corpo = await AmbienteApi.CorpoAsync(await ambiente.InscreverAsync(participante, evento));
        var pagamentoId = corpo.GetProperty("pagamento").GetProperty("id").GetGuid();

        // A reserva vence e o pagamento expira; só depois descobrimos que o Pix foi pago.
        ambiente.Relogio.Avancar(TimeSpan.FromMinutes(31));
        using (var escopo = ambiente.Servicos.CreateScope())
        {
            await escopo.ServiceProvider.GetRequiredService<ExpiracaoReservasService>().ExpirarVencidasAsync();
        }

        ambiente.Gateway.StatusNoGateway[$"stub_{pagamentoId:N}"] = StatusPagamento.Aprovado;
        using (var escopo = ambiente.Servicos.CreateScope())
        {
            Assert.Equal(1, await escopo.ServiceProvider.GetRequiredService<PagamentoService>().ReconciliarPendentesAsync(50));
        }

        // RN23: aprovação tardia não confirma a inscrição; o valor é devolvido.
        Assert.Contains(ambiente.Gateway.Estornos, e => e.IdTransacao == $"stub_{pagamentoId:N}");
        await using var contexto = banco.CriarContexto();
        Assert.NotEqual(StatusInscricao.Confirmada, (await contexto.Inscricoes.SingleAsync(i => i.Id == corpo.GetProperty("id").GetGuid())).Status);
    }

    [FactBanco]
    public async Task Reconciliacao_ignora_cobranca_recente_e_pagamento_ainda_pendente_no_gateway()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var (participante, evento) = await EventoPagoAsync(ambiente);
        var corpo = await AmbienteApi.CorpoAsync(await ambiente.InscreverAsync(participante, evento));
        var pagamentoId = corpo.GetProperty("pagamento").GetProperty("id").GetGuid();
        ambiente.Gateway.StatusNoGateway[$"stub_{pagamentoId:N}"] = StatusPagamento.Aprovado;

        using var escopo = ambiente.Servicos.CreateScope();
        var servico = escopo.ServiceProvider.GetRequiredService<PagamentoService>();

        Assert.Equal(0, await servico.ReconciliarPendentesAsync(50)); // recém-criada: o webhook ainda pode chegar

        ambiente.Gateway.StatusNoGateway.Clear();
        ambiente.Relogio.Avancar(TimeSpan.FromMinutes(5));
        Assert.Equal(0, await servico.ReconciliarPendentesAsync(50)); // continua pendente no gateway
    }
}
