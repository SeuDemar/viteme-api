using Viteme.Domain.Entities;

namespace Viteme.Application.Interfaces;

/// <summary>Dados públicos de um certificado, para a validação por código (E39).</summary>
public sealed record CertificadoValidacao(
    string Codigo, string Participante, string Evento, DateTime DataEvento, short? CargaHoraria, DateTime EmitidoEm);

public interface ICertificadoRepository
{
    Task<Certificado?> ObterPorInscricaoAsync(Guid inscricaoId, CancellationToken cancellationToken = default);

    Task<CertificadoValidacao?> ObterValidacaoPorCodigoAsync(string codigo, CancellationToken cancellationToken = default);

    void Adicionar(Certificado certificado);
}
