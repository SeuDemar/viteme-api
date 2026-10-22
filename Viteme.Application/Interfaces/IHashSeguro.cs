namespace Viteme.Application.Interfaces;

/// <summary>Hash determinístico com chave secreta (HMAC) para refresh tokens e códigos de verificação.</summary>
public interface IHashSeguro
{
    string Calcular(string valor);

    bool Confere(string valor, string hashEsperado);
}
