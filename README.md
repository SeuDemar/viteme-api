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
dotnet run --project Viteme.Api   # GET /api/v1/saude
```

## Configuração e segredos

Nada de segredo no repositório. Em desenvolvimento use `dotnet user-secrets` (projeto `Viteme.Api`); em produção, variáveis de ambiente.

| Variável | Uso |
|---|---|
| `ConnectionStrings__Default` | PostgreSQL (Supabase). Usar conexão direta ou session pooler (porta 5432), **nunca** o pooler em modo transaction (6543) |
| `Jwt__Chave`, `Jwt__Emissor`, `Jwt__Audiencia` | Autenticação (a partir da etapa 2) |
| `Gateway__Asaas__ApiKey`, `Gateway__Asaas__WebhookToken` | Gateway de pagamento (quando houver adaptador real) |
| `Cors__OrigemFrontend` | Origem permitida do frontend |

## Estado

Etapa 0 (scaffold) concluída. Próximo: etapa 1 (Domain + DbContext + migration inicial). Ver [roadmap](.claude/planning/roadmap.md).
