# viteme-api

API do **Viteme**: site que centraliza divulgação, inscrição e acompanhamento dos eventos da faculdade, com avisos de alteração e sem taxa da plataforma.

Backend em **.NET 10 (ASP.NET Core)**, PostgreSQL (Supabase) via EF Core, monólito modular em camadas. O frontend (React) fica em outro repositório e ainda não foi iniciado.

## Documentação

Especificação e planejamento em [`.claude/planning/`](.claude/planning/README.md): arquitetura, modelo de banco, endpoints, regras de negócio, segurança, testes, roadmap e pendências.

## Estrutura

| Projeto | Responsabilidade |
|---|---|
| `Viteme.Domain` | Entidades, enums e regras da própria entidade |
| `Viteme.Application` | Services, DTOs, validadores e interfaces (portas) |
| `Viteme.Infrastructure` | EF Core, repositories, adaptadores (gateway, SMS) e tarefas em segundo plano |
| `Viteme.Api` | Controllers, middlewares, autenticação, CORS, rate limiting |
| `Viteme.Tests` | Testes unitários e de integração |

Dependências: `Api → Application, Infrastructure`; `Infrastructure → Application, Domain`; `Application → Domain`.

## Requisitos

- SDK .NET 10 (ver `global.json`)

## Comandos

```bash
dotnet build Viteme.sln
dotnet test Viteme.sln
dotnet run --project Viteme.Api   # GET /api/v1/saude, Swagger em /swagger
```

### Documentação interativa (Swagger)

`/swagger` (UI) e `/openapi/v1.json`. Sempre ligado em `Development`; em outros ambientes defina `Swagger__Habilitar=true`.

### Banco de dados

O esquema vem das migrations do EF Core (`Viteme.Infrastructure/Persistencia/Migrations`). O script SQL equivalente está em [`docs/banco/schema.sql`](docs/banco/schema.sql).

```bash
# aplicar as migrations (usa ConnectionStrings__Default)
dotnet tool install --global dotnet-ef
dotnet ef database update --project Viteme.Infrastructure --startup-project Viteme.Api

# gerar uma nova migration / regenerar o script
dotnet ef migrations add NomeDaMigration --project Viteme.Infrastructure --startup-project Viteme.Api --output-dir Persistencia/Migrations
dotnet ef migrations script --project Viteme.Infrastructure --startup-project Viteme.Api -o docs/banco/schema.sql
```

### Testes

Testes unitários e de Swagger rodam sem configuração. Os testes de persistência exigem um PostgreSQL com permissão de criar bancos (cada execução cria e remove um banco temporário; **nunca aponte para o banco real**):

```bash
export VITEME_TESTES_DB="Host=localhost;Username=postgres;Password=postgres"
dotnet test Viteme.sln
```

Sem `VITEME_TESTES_DB`, esses testes são ignorados. No CI há um serviço `postgres:16`.

## Configuração e segredos

Nada de segredo no repositório. Em desenvolvimento use `dotnet user-secrets` (projeto `Viteme.Api`); em produção, variáveis de ambiente.

| Variável | Uso |
|---|---|
| `ConnectionStrings__Default` | PostgreSQL (Supabase). Usar conexão direta ou session pooler (porta 5432), **nunca** o pooler em modo transaction (6543) |
| `Jwt__Chave`, `Jwt__Emissor`, `Jwt__Audiencia` | Autenticação (a partir da etapa 2) |
| `Gateway__Asaas__ApiKey`, `Gateway__Asaas__WebhookToken` | Gateway de pagamento (quando houver adaptador real) |
| `Cors__OrigemFrontend` | Origem permitida do frontend |

## Estado

Etapas 0 (scaffold) e 1 (domínio, banco e Swagger) concluídas. Próximo: etapa 2 (autenticação e conta). Ver [roadmap](.claude/planning/roadmap.md).
