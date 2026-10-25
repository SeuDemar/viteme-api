namespace Viteme.Api.Autorizacao;

/// <summary>Políticas por papel no evento da rota (<c>{id}</c>). O papel nunca vem no token: é consultado no banco a cada requisição.</summary>
public static class Politicas
{
    public const string OrganizadorDoEvento = "OrganizadorDoEvento";
    public const string OrganizadorOuColaborador = "OrganizadorOuColaborador";
}
