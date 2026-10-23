namespace Viteme.Application.Interfaces;

/// <summary>Valores aleatórios criptograficamente seguros.</summary>
public interface IGeradorAleatorio
{
    /// <summary>Código numérico com zeros à esquerda (ex.: <c>"004821"</c>).</summary>
    string GerarCodigoNumerico(int digitos);

    /// <summary>Código alfanumérico em maiúsculas, sem caracteres ambíguos (0/O, 1/I), ex.: <c>"7KQ2M9XTR4VB"</c>.</summary>
    string GerarCodigoAlfanumerico(int tamanho);

    /// <summary>Token opaco em base64url com a quantidade de bytes informada.</summary>
    string GerarTokenOpaco(int bytes);
}
