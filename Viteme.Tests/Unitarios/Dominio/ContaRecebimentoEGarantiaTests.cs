using Viteme.Domain.Entities;
using Viteme.Domain.Enums;
using Viteme.Domain.Excecoes;
using Viteme.Tests.Suporte;

namespace Viteme.Tests.Unitarios.Dominio;

/// <summary>Fase 2/3 do Asaas: subconta do organizador e garantia do pagamento (plano pagamentos-asaas.md, seção 8.3, passo 2).</summary>
public class ContaRecebimentoEGarantiaTests
{
    private static ContaRecebimento NovaConta() =>
        new(Guid.NewGuid(), "acc_1", "wallet_1", "cifrada", "https://onboarding");

    [Fact]
    public void Conta_nasce_pendente_e_sem_data_de_aprovacao()
    {
        var conta = NovaConta();

        Assert.Equal(SituacaoContaRecebimento.Pendente, conta.Situacao);
        Assert.False(conta.EstaAprovada);
        Assert.Null(conta.AprovadaEm);
    }

    [Theory]
    [InlineData("", "wallet", "chave")]
    [InlineData("acc", " ", "chave")]
    [InlineData("acc", "wallet", "")]
    public void Conta_exige_id_carteira_e_chave_do_gateway(string conta, string wallet, string chave)
    {
        var erro = Assert.Throws<RegraDeNegocioException>(() => new ContaRecebimento(Guid.NewGuid(), conta, wallet, chave, null));
        Assert.Equal("CONTA_RECEBIMENTO_INVALIDA", erro.Codigo);
    }

    [Fact]
    public void Aprovacao_guarda_a_primeira_data_mesmo_depois_de_rejeitada_e_reaprovada()
    {
        var conta = NovaConta();

        conta.AtualizarSituacao(SituacaoContaRecebimento.Aprovada, Dados.Agora);
        conta.AtualizarSituacao(SituacaoContaRecebimento.Rejeitada, Dados.Agora.AddDays(1));
        conta.AtualizarSituacao(SituacaoContaRecebimento.Aprovada, Dados.Agora.AddDays(2));

        Assert.True(conta.EstaAprovada);
        Assert.Equal(Dados.Agora, conta.AprovadaEm);
    }

    [Fact]
    public void Garantia_e_liberada_uma_vez_para_pagamento_aprovado()
    {
        var pagamento = new Pagamento(Guid.NewGuid(), 50m);
        pagamento.Aprovar("PIX");
        pagamento.RegistrarGarantia("esc_1");
        pagamento.RegistrarGarantia("esc_1"); // webhook reenviado

        pagamento.LiberarGarantia(Dados.Agora);

        Assert.Equal("esc_1", pagamento.GarantiaId);
        Assert.Equal(Dados.Agora, pagamento.GarantiaLiberadaEm);
        Assert.Equal("GARANTIA_JA_LIBERADA",
            Assert.Throws<RegraDeNegocioException>(() => pagamento.LiberarGarantia(Dados.Agora)).Codigo);
    }

    [Fact]
    public void Outra_garantia_para_o_mesmo_pagamento_e_recusada()
    {
        var pagamento = new Pagamento(Guid.NewGuid(), 50m);
        pagamento.RegistrarGarantia("esc_1");

        Assert.Equal("GARANTIA_JA_REGISTRADA",
            Assert.Throws<RegraDeNegocioException>(() => pagamento.RegistrarGarantia("esc_2")).Codigo);
    }

    [Fact]
    public void Garantia_nao_e_liberada_sem_id_nem_com_pagamento_estornado()
    {
        var semGarantia = new Pagamento(Guid.NewGuid(), 50m);
        semGarantia.Aprovar("PIX");
        Assert.Equal("PAGAMENTO_SEM_GARANTIA",
            Assert.Throws<RegraDeNegocioException>(() => semGarantia.LiberarGarantia(Dados.Agora)).Codigo);

        var estornado = new Pagamento(Guid.NewGuid(), 50m);
        estornado.Aprovar("PIX");
        estornado.RegistrarGarantia("esc_1");
        estornado.Estornar();
        Assert.Equal("PAGAMENTO_NAO_APROVADO",
            Assert.Throws<RegraDeNegocioException>(() => estornado.LiberarGarantia(Dados.Agora)).Codigo);
    }
}
