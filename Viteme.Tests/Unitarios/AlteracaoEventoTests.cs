using Viteme.Application.Services;

namespace Viteme.Tests.Unitarios;

public class AlteracaoEventoTests
{
    [Theory]
    [InlineData("titulo", "título")]
    [InlineData("dataInicio", "início")]
    [InlineData("dataFim", "término")]
    [InlineData("local", "local")]
    [InlineData("programacao", "programação")]
    [InlineData("preco", "preço")]
    [InlineData("capacidade", "capacidade")]
    public void Notificacao_usa_o_nome_do_campo_em_portugues(string campo, string rotulo) =>
        Assert.Equal(rotulo, AlteracaoEventoRegistrador.RotuloCampo(campo));

    [Fact]
    public void Campo_desconhecido_aparece_como_veio() => Assert.Equal("outro", AlteracaoEventoRegistrador.RotuloCampo("outro"));
}
