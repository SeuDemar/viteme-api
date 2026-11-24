namespace Viteme.Application.Interfaces;

/// <summary>
/// Cifra segredos de terceiros que a aplicação precisa ler de volta (ex.: chave de API da subconta do Asaas).
/// Diferente de <see cref="IHashSeguro"/>, que é de mão única.
/// </summary>
public interface ICifradorSegredos
{
    string Cifrar(string textoClaro);

    /// <summary>Lança <see cref="System.Security.Cryptography.CryptographicException"/> se o texto foi alterado ou a chave mestra mudou.</summary>
    string Decifrar(string textoCifrado);
}
