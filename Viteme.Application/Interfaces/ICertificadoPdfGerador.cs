namespace Viteme.Application.Interfaces;

public sealed record DadosCertificado(
    string Participante, string Evento, DateTime DataInicio, DateTime DataFim, short CargaHoraria,
    string CodigoValidacao, DateTime EmitidoEm);

/// <summary>Gera o PDF do certificado. A implementação é trocável (hoje um escritor de PDF próprio, sem dependências).</summary>
public interface ICertificadoPdfGerador
{
    byte[] Gerar(DadosCertificado dados);
}
