namespace Viteme.Application.DTOs.Certificados;

/// <summary>PDF do certificado e o nome sugerido para o arquivo (E38).</summary>
public sealed record CertificadoArquivo(byte[] Conteudo, string NomeArquivo);

/// <summary>E39 — validação pública de um certificado.</summary>
public sealed record ValidacaoCertificadoResponse(
    bool Valido, string Participante, string Evento, DateTime DataEvento, short? CargaHoraria, DateTime EmitidoEm);
