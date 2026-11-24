using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Viteme.Infrastructure.Externos;
using Viteme.Infrastructure.Seguranca;

namespace Viteme.Tests.Unitarios;

/// <summary>Cifragem da chave de API da subconta (AES-256-GCM com a chave mestra da configuração).</summary>
public class CifradorSegredosTests
{
    private static CifradorSegredosAesGcm Criar(string chave) =>
        new(Options.Create(new GatewayAsaasOptions { ChaveCifragemSubcontas = chave }));

    private static string NovaChave() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    [Fact]
    public void Cifra_e_decifra_de_volta_sem_deixar_o_texto_claro_visivel()
    {
        var cifrador = Criar(NovaChave());
        const string segredo = "$aact_hmlg_000Mzk...chave-da-subconta";

        var cifrado = cifrador.Cifrar(segredo);

        Assert.DoesNotContain("chave-da-subconta", cifrado);
        Assert.Equal(segredo, cifrador.Decifrar(cifrado));
    }

    [Fact]
    public void Mesmo_texto_gera_cifras_diferentes()
    {
        var cifrador = Criar(NovaChave());

        Assert.NotEqual(cifrador.Cifrar("x"), cifrador.Cifrar("x"));
    }

    [Fact]
    public void Texto_alterado_ou_outra_chave_mestra_nao_decifram()
    {
        var cifrado = Criar(NovaChave()).Cifrar("segredo");
        var bytes = Convert.FromBase64String(cifrado);
        bytes[^1] ^= 1;

        Assert.ThrowsAny<CryptographicException>(() => Criar(NovaChave()).Decifrar(cifrado));
        Assert.ThrowsAny<CryptographicException>(() => Criar(NovaChave()).Decifrar(Convert.ToBase64String(bytes)));
        Assert.ThrowsAny<CryptographicException>(() => Criar(NovaChave()).Decifrar("nao-e-base64!"));
    }

    [Fact]
    public void Texto_alterado_com_a_mesma_chave_nao_decifra()
    {
        var cifrador = Criar(NovaChave());
        var bytes = Convert.FromBase64String(cifrador.Cifrar("segredo"));
        bytes[^1] ^= 1;

        Assert.ThrowsAny<CryptographicException>(() => cifrador.Decifrar(Convert.ToBase64String(bytes)));
    }

    [Fact]
    public void Sem_chave_mestra_a_api_sobe_mas_nao_cifra()
    {
        var cifrador = Criar("");

        Assert.Throws<InvalidOperationException>(() => cifrador.Cifrar("segredo"));
    }

    [Fact]
    public void Chave_mestra_com_tamanho_errado_e_recusada()
    {
        Assert.Throws<InvalidOperationException>(() => Criar(Convert.ToBase64String(new byte[16])));
    }
}
