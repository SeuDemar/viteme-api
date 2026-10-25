namespace Viteme.Domain.Entities;

/// <summary>Refresh token da sessão, guardado como hash.</summary>
public class TokenRenovacao
{
    public static readonly TimeSpan Validade = TimeSpan.FromDays(7);

    private TokenRenovacao()
    {
    }

    public TokenRenovacao(Guid usuarioId, string tokenHash, DateTime agora)
    {
        Id = Guid.NewGuid();
        UsuarioId = usuarioId;
        TokenHash = tokenHash;
        ExpiraEm = agora + Validade;
    }

    public Guid Id { get; private set; }

    public Guid UsuarioId { get; private set; }

    public string TokenHash { get; private set; } = null!;

    public DateTime ExpiraEm { get; private set; }

    public DateTime? RevogadoEm { get; private set; }

    public DateTime CriadoEm { get; private set; }

    public bool Revogado => RevogadoEm is not null;

    public bool EstaAtivo(DateTime agora) => !Revogado && ExpiraEm > agora;

    public void Revogar(DateTime agora) => RevogadoEm ??= agora;
}
