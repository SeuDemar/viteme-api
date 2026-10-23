using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.HttpOverrides;
using Viteme.Api.Autenticacao;
using Viteme.Api.Autorizacao;
using Viteme.Api.Configuracao;
using Viteme.Api.Documentacao;
using Viteme.Api.Middlewares;
using Viteme.Application;
using Viteme.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Enums como texto em maiúsculas com sublinhado (PENDENTE_PAGAMENTO), como na spec.
builder.Services.AddControllers().AddJsonOptions(opcoes =>
    opcoes.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseUpper)));
builder.Services.AddRespostasDeErro();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApplication();
builder.Services.AddAutenticacaoJwt();
builder.Services.AddAutorizacaoPorEvento();
builder.Services.AddLimitacaoDeRequisicoes();
builder.Services.AddDocumentacaoApi();

// Atrás de proxy/CDN o IP real vem em X-Forwarded-For; sem isso o rate limiting por IP fica ineficaz.
var confiarNoProxy = builder.Configuration.GetValue<bool>("Proxy:ConfiarEmCabecalhosEncaminhados");
if (confiarNoProxy)
{
    builder.Services.Configure<ForwardedHeadersOptions>(opcoes =>
    {
        opcoes.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        opcoes.KnownIPNetworks.Clear();
        opcoes.KnownProxies.Clear();
    });
}

var app = builder.Build();

if (confiarNoProxy)
{
    app.UseForwardedHeaders();
}

app.UseMiddleware<TratamentoExcecoesMiddleware>();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseDocumentacaoApi();

app.UseHttpsRedirection();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

// Necessário para WebApplicationFactory<Program> nos testes de integração.
public partial class Program;
