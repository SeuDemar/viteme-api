using Viteme.Infrastructure.Externos;

namespace Viteme.Tests.Unitarios;

/// <summary>Chaves da Fase 2/3 do Asaas (plano pagamentos-asaas.md, seção 8.2): desligadas por padrão e validadas na inicialização.</summary>
public class GatewayAsaasOptionsTests
{
    private static readonly string ChaveValida = Convert.ToBase64String(new byte[32]);

    [Fact]
    public void Padrao_deixa_subconta_e_garantia_desligadas_e_valido()
    {
        var opcoes = new GatewayAsaasOptions();

        Assert.False(opcoes.Subcontas.Habilitar);
        Assert.False(opcoes.Escrow.Habilitar);
        Assert.False(opcoes.Escrow.SubcontaPagaMensalidade);
        Assert.Equal(60, opcoes.Escrow.DiasParaExpirar);
        Assert.Empty(opcoes.Validar());
    }

    [Theory]
    [InlineData("")]
    [InlineData("nao-e-base64")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAA==")] // 16 bytes
    public void Subcontas_ligadas_exigem_chave_de_32_bytes(string chave)
    {
        var opcoes = new GatewayAsaasOptions { ChaveCifragemSubcontas = chave, Subcontas = { Habilitar = true } };

        Assert.Contains(opcoes.Validar(), e => e.Contains("ChaveCifragemSubcontas"));
    }

    [Fact]
    public void Subcontas_ligadas_com_chave_valida_passam()
    {
        var opcoes = new GatewayAsaasOptions { ChaveCifragemSubcontas = ChaveValida, Subcontas = { Habilitar = true } };

        Assert.Empty(opcoes.Validar());
    }

    [Fact]
    public void Garantia_sem_subconta_e_recusada()
    {
        var opcoes = new GatewayAsaasOptions { Escrow = { Habilitar = true } };

        Assert.Contains(opcoes.Validar(), e => e.Contains("Escrow:Habilitar exige"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(366)]
    public void Garantia_com_prazo_fora_do_intervalo_e_recusada(int dias)
    {
        var opcoes = new GatewayAsaasOptions
        {
            ChaveCifragemSubcontas = ChaveValida,
            Subcontas = { Habilitar = true },
            Escrow = { Habilitar = true, DiasParaExpirar = dias },
        };

        Assert.Contains(opcoes.Validar(), e => e.Contains("DiasParaExpirar"));
    }

    [Fact]
    public void Subconta_e_garantia_ligadas_com_tudo_certo_passam()
    {
        var opcoes = new GatewayAsaasOptions
        {
            ChaveCifragemSubcontas = ChaveValida,
            Subcontas = { Habilitar = true },
            Escrow = { Habilitar = true, DiasParaExpirar = 60, SubcontaPagaMensalidade = true },
        };

        Assert.Empty(opcoes.Validar());
    }
}
