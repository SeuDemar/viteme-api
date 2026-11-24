using Viteme.Application.Validacoes;

namespace Viteme.Tests.Unitarios;

public class CpfTests
{
    [Theory]
    [InlineData("12345678909")]
    [InlineData("123.456.789-09")]
    [InlineData("529.982.247-25")]
    [InlineData("11144477735")]
    public void Cpf_com_digitos_verificadores_corretos_e_valido(string cpf) => Assert.True(CpfAttribute.EhValido(cpf));

    [Theory]
    [InlineData("")]
    [InlineData("123")]
    [InlineData("12345678900")]
    [InlineData("11111111111")]
    [InlineData("000.000.000-00")]
    [InlineData("1234567890a")]
    [InlineData("123456789091")]
    public void Cpf_invalido_e_recusado(string cpf) => Assert.False(CpfAttribute.EhValido(cpf));

    [Fact]
    public void Normalizar_remove_a_mascara() => Assert.Equal("12345678909", CpfAttribute.Normalizar(" 123.456.789-09 "));

    [Fact]
    public void Atributo_aceita_nulo_e_valida_texto()
    {
        var atributo = new CpfAttribute();
        Assert.True(atributo.IsValid(null));
        Assert.True(atributo.IsValid("123.456.789-09"));
        Assert.False(atributo.IsValid("123.456.789-00"));
    }
}
