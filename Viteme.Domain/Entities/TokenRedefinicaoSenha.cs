namespace Viteme.Domain.Entities;

/// <summary>Link de redefinição de senha: uso único, curta duração, guardado como hash.</summary>
public class TokenRedefinicaoSenha
{
    public static readonly TimeSpan Validade = TimeSpan.FromHours(1);

    /// <summary>Intervalo mínimo entre dois pedidos para o mesmo usuário (evita encher a caixa de e-mail).</summary>
    public static readonly TimeSpan IntervaloMinimoEntrePedidos = TimeSpan.FromMinutes(1);

    private TokenRedefinicaoSenha()
    {
    }

    public TokenRedefinicaoSenha(Guid usuarioId, string tokenHash, DateTime agora)
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

    public DateTime? UsadoEm { get; private set; }

    public DateTime CriadoEm { get; private set; }

    public bool EstaAtivo(DateTime agora) => UsadoEm is null && ExpiraEm > agora;
}
