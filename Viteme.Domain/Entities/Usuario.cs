using Viteme.Domain.Excecoes;

namespace Viteme.Domain.Entities;

/// <summary>Pessoa cadastrada. O papel (participante, organizador ou colaborador) é definido em cada evento.</summary>
public class Usuario
{
    public const int MaxTentativasLogin = 5;

    public static readonly TimeSpan DuracaoBloqueio = TimeSpan.FromMinutes(15);

    private Usuario()
    {
    }

    public Usuario(string nome, string email, string senhaHash, string? telefone = null)
    {
        Id = Guid.NewGuid();
        Nome = nome;
        Email = email;
        SenhaHash = senhaHash;
        Telefone = telefone;
    }

    public Guid Id { get; private set; }

    public string Nome { get; private set; } = null!;

    public string Email { get; private set; } = null!;

    public string SenhaHash { get; private set; } = null!;

    public string? Telefone { get; private set; }

    public bool TelefoneVerificado { get; private set; }

    public bool AceitaSms { get; private set; }

    public DateTime? AceiteSmsEm { get; private set; }

    public short TentativasLogin { get; private set; }

    public DateTime? BloqueadoAte { get; private set; }

    public DateTime CriadoEm { get; private set; }

    public DateTime AtualizadoEm { get; private set; }

    public bool EstaBloqueado(DateTime agora) => BloqueadoAte is { } ate && ate > agora;

    /// <summary>RN03: a 5.ª tentativa inválida seguida bloqueia a conta por 15 minutos.</summary>
    public void RegistrarFalhaLogin(DateTime agora)
    {
        if (BloqueadoAte is { } ate && ate <= agora)
        {
            TentativasLogin = 0;
            BloqueadoAte = null;
        }

        TentativasLogin++;
        if (TentativasLogin >= MaxTentativasLogin)
        {
            BloqueadoAte = agora + DuracaoBloqueio;
        }
    }

    public void RegistrarLoginComSucesso()
    {
        TentativasLogin = 0;
        BloqueadoAte = null;
    }

    public void AtualizarNome(string nome) => Nome = nome;

    /// <summary>RN18: trocar o telefone desfaz a verificação e o aceite de SMS.</summary>
    public void AlterarTelefone(string? telefone)
    {
        if (telefone == Telefone)
        {
            return;
        }

        Telefone = telefone;
        TelefoneVerificado = false;
        AceitaSms = false;
        AceiteSmsEm = null;
    }

    public void MarcarTelefoneVerificado()
    {
        if (Telefone is null)
        {
            throw new RegraDeNegocioException("TELEFONE_NAO_INFORMADO", "Telefone não informado",
                "Cadastre um telefone antes de verificá-lo.");
        }

        TelefoneVerificado = true;
    }

    /// <summary>RN18: o aceite de SMS exige telefone verificado e pode ser revogado a qualquer momento.</summary>
    public void DefinirAceiteSms(bool aceita, DateTime agora)
    {
        if (aceita && !TelefoneVerificado)
        {
            throw new RegraDeNegocioException("TELEFONE_NAO_VERIFICADO", "Telefone não verificado",
                "Verifique o telefone antes de ativar o recebimento de SMS.");
        }

        AceitaSms = aceita;
        AceiteSmsEm = aceita ? agora : null;
    }

    /// <summary>RN16: SMS só é enviado com telefone verificado e aceite ativo.</summary>
    public bool PodeReceberSms => AceitaSms && TelefoneVerificado && Telefone is not null;
}
