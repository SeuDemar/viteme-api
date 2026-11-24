using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Viteme.Application.Interfaces;
using Viteme.Infrastructure.Externos;

namespace Viteme.Infrastructure.Seguranca;

/// <summary>
/// AES-256-GCM com a chave mestra <c>Gateway:Asaas:ChaveCifragemSubcontas</c> (32 bytes em base64, só por variável de ambiente).
/// Formato gravado: base64( versão[1] | nonce[12] | tag[16] | texto cifrado ). A versão permite trocar o esquema depois.
/// Sem a chave configurada, a aplicação sobe normalmente e só falha ao cifrar ou decifrar (a Fase 2 fica desligada).
/// </summary>
public sealed class CifradorSegredosAesGcm : ICifradorSegredos
{
    private const byte Versao = 1;
    private const int TamanhoNonce = 12;
    private const int TamanhoTag = 16;
    private const int TamanhoCabecalho = 1 + TamanhoNonce + TamanhoTag;

    private readonly byte[]? _chave;

    public CifradorSegredosAesGcm(IOptions<GatewayAsaasOptions> opcoes)
    {
        var configurada = opcoes.Value.ChaveCifragemSubcontas;
        if (!string.IsNullOrWhiteSpace(configurada))
        {
            var bytes = Convert.FromBase64String(configurada.Trim());
            if (bytes.Length != 32)
            {
                throw new InvalidOperationException("Gateway:Asaas:ChaveCifragemSubcontas deve ter 32 bytes em base64.");
            }

            _chave = bytes;
        }
    }

    public string Cifrar(string textoClaro)
    {
        ArgumentNullException.ThrowIfNull(textoClaro);
        var chave = ObterChave();

        var claro = Encoding.UTF8.GetBytes(textoClaro);
        var saida = new byte[TamanhoCabecalho + claro.Length];
        saida[0] = Versao;
        var nonce = saida.AsSpan(1, TamanhoNonce);
        var tag = saida.AsSpan(1 + TamanhoNonce, TamanhoTag);
        var cifrado = saida.AsSpan(TamanhoCabecalho);
        RandomNumberGenerator.Fill(nonce);

        using var aes = new AesGcm(chave, TamanhoTag);
        aes.Encrypt(nonce, claro, cifrado, tag, [Versao]);
        return Convert.ToBase64String(saida);
    }

    public string Decifrar(string textoCifrado)
    {
        ArgumentNullException.ThrowIfNull(textoCifrado);
        var chave = ObterChave();

        byte[] entrada;
        try
        {
            entrada = Convert.FromBase64String(textoCifrado);
        }
        catch (FormatException ex)
        {
            throw new CryptographicException("Segredo cifrado em formato inválido.", ex);
        }

        if (entrada.Length < TamanhoCabecalho || entrada[0] != Versao)
        {
            throw new CryptographicException("Segredo cifrado em formato ou versão desconhecida.");
        }

        var claro = new byte[entrada.Length - TamanhoCabecalho];
        using var aes = new AesGcm(chave, TamanhoTag);
        aes.Decrypt(
            entrada.AsSpan(1, TamanhoNonce),
            entrada.AsSpan(TamanhoCabecalho),
            entrada.AsSpan(1 + TamanhoNonce, TamanhoTag),
            claro,
            [Versao]);
        return Encoding.UTF8.GetString(claro);
    }

    private byte[] ObterChave() => _chave ?? throw new InvalidOperationException(
        "Gateway:Asaas:ChaveCifragemSubcontas não está configurada (necessária para a conta de recebimento).");
}
