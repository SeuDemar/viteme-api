using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Viteme.Application.Interfaces;
using Viteme.Infrastructure.Externos;

namespace Viteme.Tests.Unitarios;

public class CertificadoPdfGeradorTests
{
    private static readonly CertificadoPdfGerador Gerador = new(Options.Create(new CertificadosOptions { UrlValidacao = "https://exemplo.test/validar" }));

    private static DadosCertificado Dados(string participante = "José da Conceição", string evento = "Semana Acadêmica") => new(
        participante, evento, new DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 10, 20, 0, 0, DateTimeKind.Utc),
        8, "7KQ2M9XTR4VBN3HD", new DateTime(2026, 11, 11, 15, 0, 0, DateTimeKind.Utc));

    [Fact]
    public void Gera_um_pdf_com_cabecalho_xref_e_fim_de_arquivo()
    {
        var pdf = Gerador.Gerar(Dados());
        var texto = Encoding.Latin1.GetString(pdf);

        Assert.StartsWith("%PDF-1.4", texto);
        Assert.EndsWith("%%EOF\n", texto);
        var inicioXref = int.Parse(Regex.Match(texto, @"startxref\n(\d+)\n").Groups[1].Value);
        Assert.Equal("xref", texto.Substring(inicioXref, 4));
    }

    [Fact]
    public void As_posicoes_do_xref_apontam_para_os_objetos()
    {
        var texto = Encoding.Latin1.GetString(Gerador.Gerar(Dados()));
        var entradas = Regex.Matches(texto, @"(\d{10}) 00000 n ");

        Assert.Equal(7, entradas.Count);
        for (var i = 0; i < entradas.Count; i++)
        {
            Assert.StartsWith($"{i + 1} 0 obj", texto[int.Parse(entradas[i].Groups[1].Value)..]);
        }
    }

    [Fact]
    public void O_comprimento_declarado_do_conteudo_bate_com_o_stream()
    {
        var texto = Encoding.Latin1.GetString(Gerador.Gerar(Dados()));
        var m = Regex.Match(texto, @"<< /Length (\d+) >>\nstream\n(.*?)endstream", RegexOptions.Singleline);

        Assert.Equal(int.Parse(m.Groups[1].Value), m.Groups[2].Value.Length);
    }

    [Fact]
    public void Contem_participante_evento_carga_horaria_codigo_e_url()
    {
        var texto = Encoding.Latin1.GetString(Gerador.Gerar(Dados()));

        Assert.Contains("Jos\\351 da Concei\\347\\343o", texto); // letras acentuadas em WinAnsi (octal)
        Assert.Contains("Semana Acad\\352mica", texto);
        Assert.Contains("carga hor\\341ria de 8 hora\\(s\\)", texto);
        Assert.Contains("7KQ2M9XTR4VBN3HD", texto);
        Assert.Contains("https://exemplo.test/validar", texto);
        Assert.Contains("10/11/2026", texto);
    }

    [Fact]
    public void Parenteses_e_barras_no_texto_sao_escapados()
    {
        var texto = Encoding.Latin1.GetString(Gerador.Gerar(Dados(participante: @"Ana (Teste) \ Silva")));

        Assert.Contains(@"Ana \(Teste\) \\ Silva", texto);
    }

    [Fact]
    public void Titulo_muito_longo_e_quebrado_em_linhas_e_o_documento_continua_valido()
    {
        var titulo = string.Join(' ', Enumerable.Repeat("Congresso Interdisciplinar de Tecnologia e Inovação", 6));

        var texto = Encoding.Latin1.GetString(Gerador.Gerar(Dados(evento: titulo)));

        Assert.True(Regex.Matches(texto, "Tj ET").Count <= 12); // no máximo 3 linhas de título
        Assert.Contains("\\205", texto); // reticências quando excede 3 linhas
        Assert.EndsWith("%%EOF\n", texto);
    }
}
