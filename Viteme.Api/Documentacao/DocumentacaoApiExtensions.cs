using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Viteme.Api.Documentacao;

/// <summary>Documentação da API: OpenAPI nativo do ASP.NET Core + Swagger UI em <c>/swagger</c>.</summary>
public static class DocumentacaoApiExtensions
{
    private const string EsquemaBearer = "Bearer";

    public static IServiceCollection AddDocumentacaoApi(this IServiceCollection servicos)
    {
        servicos.AddOpenApi(opcoes =>
        {
            opcoes.OpenApiVersion = OpenApiSpecVersion.OpenApi3_0;

            opcoes.AddDocumentTransformer((documento, _, _) =>
            {
                documento.Info = new OpenApiInfo
                {
                    Title = "Viteme API",
                    Version = "v1",
                    Description = "API do Viteme: divulgação, inscrição e acompanhamento dos eventos da faculdade. "
                        + "Erros seguem o padrão ProblemDetails (RFC 9457) com o campo adicional `codigo` nas regras de negócio. "
                        + "Para chamar endpoints protegidos, faça login (`POST /api/v1/auth/login`) e informe o `tokenAcesso` em **Authorize**.",
                };

                documento.Components ??= new OpenApiComponents();
                documento.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
                documento.Components.SecuritySchemes[EsquemaBearer] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "JWT",
                    Description = "Token de acesso JWT (15 minutos). Informe somente o token, sem o prefixo `Bearer`.",
                };

                return Task.CompletedTask;
            });

            // Marca com cadeado apenas os endpoints que exigem autenticação.
            opcoes.AddOperationTransformer((operacao, contexto, _) =>
            {
                var metadados = contexto.Description.ActionDescriptor.EndpointMetadata;
                var exigeAutenticacao = metadados.OfType<IAuthorizeData>().Any()
                    && !metadados.OfType<IAllowAnonymous>().Any();

                if (exigeAutenticacao && contexto.Document is { } documento)
                {
                    operacao.Security ??= [];
                    operacao.Security.Add(new OpenApiSecurityRequirement
                    {
                        [new OpenApiSecuritySchemeReference(EsquemaBearer, documento)] = [],
                    });
                }

                return Task.CompletedTask;
            });
        });

        return servicos;
    }

    /// <summary>Em desenvolvimento sempre; nos demais ambientes só com <c>Swagger:Habilitar=true</c> (ex.: demonstração).</summary>
    public static WebApplication UseDocumentacaoApi(this WebApplication app)
    {
        var habilitado = app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Swagger:Habilitar");
        if (!habilitado)
        {
            return app;
        }

        app.MapOpenApi();
        app.UseSwaggerUI(opcoes =>
        {
            opcoes.SwaggerEndpoint("/openapi/v1.json", "Viteme API v1");
            opcoes.RoutePrefix = "swagger";
            opcoes.DocumentTitle = "Viteme API";
            opcoes.EnablePersistAuthorization();
            opcoes.DisplayRequestDuration();
        });

        return app;
    }
}
