using Viteme.Domain.Entities;

namespace Viteme.Application.Interfaces;

public interface IUsuarioRepository
{
    Task<Usuario?> ObterPorIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Busca sem diferenciar maiúsculas e minúsculas (RN01).</summary>
    Task<Usuario?> ObterPorEmailAsync(string email, CancellationToken cancellationToken = default);

    Task<bool> EmailExisteAsync(string email, CancellationToken cancellationToken = default);

    void Adicionar(Usuario usuario);
}
