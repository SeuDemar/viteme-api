namespace Viteme.Domain.Entities;

/// <summary>Certificado de participação, com código público de validação (RN21).</summary>
public class Certificado
{
    private Certificado()
    {
    }

    public Certificado(Guid inscricaoId, string codigoValidacao, DateTime agora)
    {
        Id = Guid.NewGuid();
        InscricaoId = inscricaoId;
        CodigoValidacao = codigoValidacao;
        EmitidoEm = agora;
    }

    public Guid Id { get; private set; }

    public Guid InscricaoId { get; private set; }

    public string CodigoValidacao { get; private set; } = null!;

    public DateTime EmitidoEm { get; private set; }
}
