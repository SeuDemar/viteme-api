namespace Viteme.Domain.Entities;

/// <summary>Código de 6 dígitos enviado por SMS para confirmar o telefone (guardado só como hash).</summary>
public class CodigoVerificacao
{
    public const short MaxTentativas = 5;

    public static readonly TimeSpan Validade = TimeSpan.FromMinutes(10);

    public static readonly TimeSpan IntervaloEntreEnvios = TimeSpan.FromSeconds(60);

    private CodigoVerificacao()
    {
    }

    public CodigoVerificacao(Guid usuarioId, string codigoHash, DateTime agora)
    {
        Id = Guid.NewGuid();
        UsuarioId = usuarioId;
        CodigoHash = codigoHash;
        ExpiraEm = agora + Validade;
    }

    public Guid Id { get; private set; }

    public Guid UsuarioId { get; private set; }

    public string CodigoHash { get; private set; } = null!;

    public DateTime ExpiraEm { get; private set; }

    public short Tentativas { get; private set; }

    public bool Utilizado { get; private set; }

    public DateTime CriadoEm { get; private set; }

    public bool Expirado(DateTime agora) => ExpiraEm <= agora;

    public bool TentativasEsgotadas => Tentativas >= MaxTentativas;

    public bool EstaValido(DateTime agora) => !Utilizado && !Expirado(agora) && !TentativasEsgotadas;

    public void RegistrarTentativa() => Tentativas++;

    public void MarcarComoUtilizado() => Utilizado = true;

    public void Invalidar() => Utilizado = true;
}
