namespace Viteme.Application.Excecoes;

/// <summary>
/// O gateway recusou os dados enviados (ex.: CNPJ já usado por outra conta no Asaas). <see cref="Exception.Message"/> traz o
/// motivo como o gateway o descreveu, em texto para o usuário; quem chama devolve 422 em vez de "gateway indisponível".
/// </summary>
public sealed class DadosRecusadosPeloGatewayException(string motivo, Exception? interna = null) : Exception(motivo, interna);
