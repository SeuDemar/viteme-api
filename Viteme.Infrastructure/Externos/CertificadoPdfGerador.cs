using System.Globalization;
using System.Text;
using Microsoft.Extensions.Options;
using Viteme.Application.Interfaces;

namespace Viteme.Infrastructure.Externos;

/// <summary>Seção <c>Certificados</c> da configuração.</summary>
public class CertificadosOptions
{
    public const string Secao = "Certificados";

    /// <summary>Endereço da página pública de validação, impresso no certificado.</summary>
    public string UrlValidacao { get; set; } = "https://viteme.app/validar";
}

/// <summary>
/// Gera o certificado como um PDF de uma página (A4 paisagem) escrito à mão: texto em Helvetica, sem imagens nem
/// bibliotecas externas. Ficar atrás de <see cref="ICertificadoPdfGerador"/> permite trocar por outro gerador depois.
/// </summary>
public class CertificadoPdfGerador(IOptions<CertificadosOptions> opcoes) : ICertificadoPdfGerador
{
    private const double Largura = 842;
    private const double Altura = 595;
    private const double MargemTexto = 90;

    // Larguras (em milésimos de em) da Helvetica e da Helvetica-Bold para os caracteres ASCII 32..126.
    private static readonly int[] LarguraNormal =
    [
        278, 278, 355, 556, 556, 889, 667, 191, 333, 333, 389, 584, 278, 333, 278, 278,
        556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 278, 278, 584, 584, 584, 556,
        1015, 667, 667, 722, 722, 667, 611, 778, 722, 278, 500, 667, 556, 833, 722, 778,
        667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 278, 278, 278, 469, 556,
        333, 556, 556, 500, 556, 556, 278, 556, 556, 222, 222, 500, 222, 833, 556, 556,
        556, 556, 333, 500, 278, 556, 500, 722, 500, 500, 500, 334, 260, 334, 584,
    ];

    private static readonly int[] LarguraNegrito =
    [
        278, 333, 474, 556, 556, 889, 722, 238, 333, 333, 389, 584, 278, 333, 278, 278,
        556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 333, 333, 584, 584, 584, 611,
        975, 722, 722, 722, 722, 667, 611, 778, 722, 278, 556, 722, 611, 833, 722, 778,
        667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 333, 278, 333, 584, 556,
        333, 556, 611, 556, 611, 556, 333, 611, 611, 278, 278, 556, 278, 889, 611, 611,
        611, 611, 389, 556, 333, 611, 556, 778, 556, 556, 500, 389, 280, 389, 584,
    ];

    public byte[] Gerar(DadosCertificado dados)
    {
        var conteudo = new StringBuilder();
        Moldura(conteudo);

        Centralizado(conteudo, "CERTIFICADO DE PARTICIPAÇÃO", 34, true, 470, 0.10, 0.25, 0.45);
        Centralizado(conteudo, "Certificamos que", 16, false, 410);
        Centralizado(conteudo, dados.Participante, 28, true, 370, 0.10, 0.25, 0.45);
        Centralizado(conteudo, "participou do evento", 16, false, 330);

        var linha = 296.0;
        foreach (var trecho in Quebrar(dados.Evento, 24, true, Largura - 2 * MargemTexto))
        {
            Centralizado(conteudo, trecho, 24, true, linha);
            linha -= 30;
        }

        var cultura = CultureInfo.InvariantCulture; // o formato dd/MM/yyyy é explícito; não depende de cultura instalada
        var periodo = dados.DataInicio.Date == dados.DataFim.Date
            ? $"realizado em {Brasilia(dados.DataInicio).ToString("dd/MM/yyyy", cultura)}"
            : $"realizado de {Brasilia(dados.DataInicio).ToString("dd/MM/yyyy", cultura)} a {Brasilia(dados.DataFim).ToString("dd/MM/yyyy", cultura)}";
        Centralizado(conteudo, $"{periodo}, com carga horária de {dados.CargaHoraria} hora(s).", 16, false, Math.Min(linha - 10, 230));

        Centralizado(conteudo, $"Emitido em {Brasilia(dados.EmitidoEm).ToString("dd/MM/yyyy", cultura)}", 12, false, 130);
        Centralizado(conteudo, $"Código de validação: {dados.CodigoValidacao}", 14, true, 100, 0.10, 0.25, 0.45);
        Centralizado(conteudo, $"Confira a autenticidade em {opcoes.Value.UrlValidacao}", 11, false, 78);

        return Montar(conteudo.ToString(), $"Certificado {dados.CodigoValidacao}");
    }

    private static DateTime Brasilia(DateTime utc) => utc.AddHours(-3);

    private static void Moldura(StringBuilder c)
    {
        c.Append("0.10 0.25 0.45 RG 3 w 30 30 782 535 re S\n");
        c.Append("0.10 0.25 0.45 RG 1 w 40 40 762 515 re S\n");
    }

    private static void Centralizado(
        StringBuilder c, string texto, int tamanho, bool negrito, double y, double r = 0, double g = 0, double b = 0)
    {
        var x = (Largura - Medir(texto, tamanho, negrito)) / 2;
        c.Append(CultureInfo.InvariantCulture, $"BT {r:0.##} {g:0.##} {b:0.##} rg /{(negrito ? "F2" : "F1")} {tamanho} Tf {x:0.##} {y:0.##} Td (");
        c.Append(Escapar(texto)).Append(") Tj ET\n");
    }

    private static double Medir(string texto, int tamanho, bool negrito)
    {
        var tabela = negrito ? LarguraNegrito : LarguraNormal;
        var total = 0;
        foreach (var ch in Normalizar(texto))
        {
            total += ch is >= ' ' and <= '~' ? tabela[ch - ' '] : 556;
        }

        return total * tamanho / 1000.0;
    }

    /// <summary>Mede com as letras sem acento (a largura de "é" é a de "e").</summary>
    private static string Normalizar(string texto)
    {
        var sb = new StringBuilder(texto.Length);
        foreach (var ch in texto.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(ch);
            }
        }

        return sb.ToString();
    }

    private static List<string> Quebrar(string texto, int tamanho, bool negrito, double larguraMaxima)
    {
        var linhas = new List<string>();
        var atual = new StringBuilder();

        foreach (var palavra in texto.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var tentativa = atual.Length == 0 ? palavra : $"{atual} {palavra}";
            if (atual.Length > 0 && Medir(tentativa, tamanho, negrito) > larguraMaxima)
            {
                linhas.Add(atual.ToString());
                atual.Clear().Append(palavra);
            }
            else
            {
                atual.Clear().Append(tentativa);
            }
        }

        if (atual.Length > 0)
        {
            linhas.Add(atual.ToString());
        }

        // Evita estourar a página com títulos enormes: o título já é limitado na criação do evento.
        return linhas.Count > 3 ? [.. linhas.Take(2), linhas[2] + "…"] : linhas;
    }

    /// <summary>Escapa o texto para uma string PDF em WinAnsi (Latin-1 cobre as letras acentuadas do português).</summary>
    private static string Escapar(string texto)
    {
        var sb = new StringBuilder(texto.Length);
        foreach (var ch in texto)
        {
            switch (ch)
            {
                case '(' or ')' or '\\':
                    sb.Append('\\').Append(ch);
                    break;
                case '…':
                    sb.Append("\\205"); // reticências em WinAnsi
                    break;
                case < ' ':
                    sb.Append(' ');
                    break;
                case <= '~':
                    sb.Append(ch);
                    break;
                case <= 'ÿ':
                    sb.Append('\\').Append(Convert.ToString(ch, 8).PadLeft(3, '0'));
                    break;
                default:
                    sb.Append('?');
                    break;
            }
        }

        return sb.ToString();
    }

    private static byte[] Montar(string conteudo, string titulo)
    {
        var objetos = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {Largura:0} {Altura:0}] /Contents 4 0 R "
                + "/Resources << /Font << /F1 5 0 R /F2 6 0 R >> >> >>",
            $"<< /Length {Encoding.Latin1.GetByteCount(conteudo)} >>\nstream\n{conteudo}endstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>",
            $"<< /Title ({Escapar(titulo)}) /Producer (Viteme) >>",
        };

        using var saida = new MemoryStream();
        void Escrever(string s) => saida.Write(Encoding.Latin1.GetBytes(s));

        Escrever("%PDF-1.4\n");
        var posicoes = new List<long>();
        for (var i = 0; i < objetos.Count; i++)
        {
            posicoes.Add(saida.Position);
            Escrever($"{i + 1} 0 obj\n{objetos[i]}\nendobj\n");
        }

        var inicioXref = saida.Position;
        Escrever($"xref\n0 {objetos.Count + 1}\n0000000000 65535 f \n");
        foreach (var p in posicoes)
        {
            Escrever($"{p:D10} 00000 n \n");
        }

        Escrever($"trailer\n<< /Size {objetos.Count + 1} /Root 1 0 R /Info 7 0 R >>\nstartxref\n{inicioXref}\n%%EOF\n");
        return saida.ToArray();
    }
}
