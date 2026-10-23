using Viteme.Domain.Entities;

namespace Viteme.Application.Interfaces;

public interface ICodigoVerificacaoRepository
{
    /// <summary>Último código emitido para o usuário, usado ou não (controle do intervalo de 60 s, RN18).</summary>
    Task<CodigoVerificacao?> ObterUltimoAsync(Guid usuarioId, CancellationToken cancellationToken = default);

    /// <summary>Último código ainda não utilizado nem invalidado.</summary>
    Task<CodigoVerificacao?> ObterUltimoPendenteAsync(Guid usuarioId, CancellationToken cancellationToken = default);

    /// <summary>Invalida todos os códigos pendentes (novo código emitido ou telefone trocado).</summary>
    Task InvalidarPendentesAsync(Guid usuarioId, CancellationToken cancellationToken = default);

    /// <summary>Códigos pedidos desde <paramref name="desde"/> por contas que hoje usam este telefone (anti-abuso, P20).</summary>
    Task<int> ContarEnviosParaTelefoneAsync(string telefone, DateTime desde, CancellationToken cancellationToken = default);

    void Adicionar(CodigoVerificacao codigo);
}
