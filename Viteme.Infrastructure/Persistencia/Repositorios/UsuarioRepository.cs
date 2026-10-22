using Microsoft.EntityFrameworkCore;
using Viteme.Application.Interfaces;
using Viteme.Domain.Entities;

namespace Viteme.Infrastructure.Persistencia.Repositorios;

public class UsuarioRepository(VitemeDbContext contexto) : IUsuarioRepository
{
    public Task<Usuario?> ObterPorIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        contexto.Usuarios.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

    // lower(email) casa com o índice único ux_usuario_email_lower (RI02).
    public Task<Usuario?> ObterPorEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        var normalizado = email.ToLower();
        return contexto.Usuarios.FirstOrDefaultAsync(u => u.Email.ToLower() == normalizado, cancellationToken);
    }

    public Task<bool> EmailExisteAsync(string email, CancellationToken cancellationToken = default)
    {
        var normalizado = email.ToLower();
        return contexto.Usuarios.AnyAsync(u => u.Email.ToLower() == normalizado, cancellationToken);
    }

    public void Adicionar(Usuario usuario) => contexto.Usuarios.Add(usuario);
}
