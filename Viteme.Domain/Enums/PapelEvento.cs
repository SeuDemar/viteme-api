namespace Viteme.Domain.Enums;

/// <summary>Papel do usuário em um evento (RN04). Não é gravado: deriva do organizador, dos colaboradores e das inscrições.</summary>
public enum PapelEvento
{
    Participante,
    Colaborador,
    Organizador,
}
