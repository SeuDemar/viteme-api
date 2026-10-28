using System.Collections.Concurrent;
using Viteme.Application.Interfaces;

namespace Viteme.Tests.Suporte;

/// <summary>Consulta de CNPJ falsa: por padrão todo CNPJ é ativo; o teste pode cadastrar respostas ou simular falha.</summary>
public class CnpjEmMemoria : IConsultaCnpj
{
    /// <summary>Respostas por CNPJ; <c>null</c> = desconhecido na Receita.</summary>
    public ConcurrentDictionary<string, DadosCnpj?> Respostas { get; } = new();

    public ConcurrentQueue<string> Consultados { get; } = new();

    public bool Falhar { get; set; }

    public Task<DadosCnpj?> ConsultarAsync(string cnpj, CancellationToken cancellationToken = default)
    {
        Consultados.Enqueue(cnpj);
        if (Falhar)
        {
            throw new HttpRequestException("consulta indisponível");
        }

        return Task.FromResult(Respostas.TryGetValue(cnpj, out var dados)
            ? dados
            : new DadosCnpj("Centro Acadêmico de Teste", true, "ATIVA"));
    }
}
