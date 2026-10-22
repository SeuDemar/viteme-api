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

```bash
cd Viteme.Api
dotnet user-secrets init
dotnet user-secrets set "ConnectionStrings:Default" "Host=...;Port=5432;Database=...;Username=...;Password=...;SSL Mode=Require"
dotnet user-secrets set "Jwt:Chave" "$(openssl rand -base64 48)"   # mínimo de 32 caracteres
```

A API **não inicia a autenticação sem `Jwt:Chave`**. Valores não secretos (emissor, audiência, limites de rate limiting, SMS stub) ficam em `appsettings*.json`.

| Variável | Uso |
|---|---|
| `ConnectionStrings__Default` | PostgreSQL (Supabase). Usar conexão direta ou session pooler (porta 5432), **nunca** o pooler em modo transaction (6543) |
| `Jwt__Chave` | Chave HS256 (≥ 32 caracteres). Também deriva a chave do HMAC dos refresh tokens e códigos |
| `Jwt__Emissor`, `Jwt__Audiencia` | Opcionais (padrões `viteme-api` e `viteme-web`) |
| `Sms__LogarConteudo` | `true` só em desenvolvimento: o SMS stub mostra o código no log |
| `RateLimiting__Habilitar` | Padrão `true`. Limites: login 5/min e cadastro 10/min por IP; código SMS 5/min por usuário |
| `Proxy__ConfiarEmCabecalhosEncaminhados` | `true` atrás de proxy/CDN, para o rate limiting ver o IP real |
| `Gateway__Asaas__ApiKey`, `Gateway__Asaas__WebhookToken` | Gateway de pagamento (quando houver adaptador real) |
| `Cors__OrigemFrontend` | Origem permitida do frontend |

## Endpoints implementados

| Grupo | Endpoints |
|---|---|
| Autenticação | `POST /api/v1/auth/cadastro`, `login`, `renovar`, `logout` |
| Conta | `GET/PUT /api/v1/usuarios/me`, `POST /usuarios/me/telefone/codigo`, `POST /usuarios/me/telefone/verificar`, `PUT /usuarios/me/preferencias-sms` |
| Infra | `GET /api/v1/saude` |

Documentação interativa completa em `/swagger`. Os demais (E10–E39) seguem o [roadmap](.claude/planning/roadmap.md).

## Estado

Etapas 0 (scaffold), 1 (domínio, banco e Swagger) e 2 (autenticação e conta, E01–E09) concluídas. Próximo: etapa 3 (eventos e programação). Ver [roadmap](.claude/planning/roadmap.md).
