using Microsoft.EntityFrameworkCore;
using Viteme.Application.Interfaces;
using Viteme.Domain.Entities;

namespace Viteme.Infrastructure.Persistencia.Repositorios;

public class CertificadoRepository(VitemeDbContext contexto) : ICertificadoRepository
{
    public Task<Certificado?> ObterPorInscricaoAsync(Guid inscricaoId, CancellationToken cancellationToken = default) =>
        contexto.Certificados.AsNoTracking().FirstOrDefaultAsync(c => c.InscricaoId == inscricaoId, cancellationToken);

    public Task<CertificadoValidacao?> ObterValidacaoPorCodigoAsync(string codigo, CancellationToken cancellationToken = default) =>
        (from c in contexto.Certificados.AsNoTracking()
         where c.CodigoValidacao == codigo
         join i in contexto.Inscricoes.AsNoTracking() on c.InscricaoId equals i.Id
         join e in contexto.Eventos.AsNoTracking() on i.EventoId equals e.Id
         join u in contexto.Usuarios.AsNoTracking() on i.UsuarioId equals u.Id
         select new CertificadoValidacao(c.CodigoValidacao, u.Nome, e.Titulo, e.DataInicio, e.CargaHoraria, c.EmitidoEm))
        .FirstOrDefaultAsync(cancellationToken);

    public void Adicionar(Certificado certificado) => contexto.Certificados.Add(certificado);
}
