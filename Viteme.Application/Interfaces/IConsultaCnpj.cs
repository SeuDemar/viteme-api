namespace Viteme.Application.Interfaces;

/// <summary>Situação cadastral de um CNPJ na Receita Federal.</summary>
public sealed record DadosCnpj(string RazaoSocial, bool Ativo, string Situacao);

/// <summary>Consulta pública de CNPJ (hoje a BrasilAPI). Lança exceção quando o serviço está indisponível.</summary>
public interface IConsultaCnpj
{
    /// <summary>Dados do CNPJ (14 caracteres, sem máscara) ou <c>null</c> se a Receita não o conhece.</summary>
    Task<DadosCnpj?> ConsultarAsync(string cnpj, CancellationToken cancellationToken = default);
}
