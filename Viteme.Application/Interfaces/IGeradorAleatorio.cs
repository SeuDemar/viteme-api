namespace Viteme.Application.Interfaces;

/// <summary>Valores aleatórios criptograficamente seguros.</summary>
public interface IGeradorAleatorio
{
    /// <summary>Código numérico com zeros à esquerda (ex.: <c>"004821"</c>).</summary>
    string GerarCodigoNumerico(int digitos);

    /// <summary>Token opaco em base64url com a quantidade de bytes informada.</summary>
    string GerarTokenOpaco(int bytes);
}
