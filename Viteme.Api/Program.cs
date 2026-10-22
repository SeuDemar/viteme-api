using Viteme.Api.Documentacao;
using Viteme.Api.Middlewares;
using Viteme.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddDocumentacaoApi();

var app = builder.Build();

app.UseMiddleware<TratamentoExcecoesMiddleware>();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseDocumentacaoApi();

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();

// Necessário para WebApplicationFactory<Program> nos testes de integração.
public partial class Program;
