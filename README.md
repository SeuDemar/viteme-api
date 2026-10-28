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
| `Tarefas__Habilitar` | Padrão `true`. Liga o encerramento automático de eventos (a cada 15 min, `Tarefas__IntervaloEncerramentoMinutos`) |
| `Gateway__Stub__SegredoWebhook` | Segredo do HMAC dos webhooks do gateway stub (vazio = segredo efêmero, só o simulador interno assina) |
| `Gateway__Stub__HabilitarSimulador` | `true` só em demonstração/desenvolvimento: liga o simulador de pagamento. **Nunca em produção** |
| `Tarefas__IntervaloReservasMinutos` | Padrão 1. Expira reservas vencidas e repete estornos pendentes |
| `Tarefas__IntervaloSmsMinutos`, `Tarefas__IntervaloLembretesMinutos` | Padrões 1 e 15. Entrega de SMS (com novas tentativas) e lembretes de 24 h |
| `Certificados__UrlValidacao` | Endereço da página pública de validação, impresso no PDF do certificado |
| `RateLimiting__Habilitar` | Padrão `true`. Limites: login 5/min e cadastro 10/min por IP; código SMS 5/min, denúncias 5/min e consulta de CNPJ 5/min por usuário; validação de certificado 30/min por IP |
| `Proxy__ConfiarEmCabecalhosEncaminhados` | `true` atrás de proxy/CDN, para o rate limiting ver o IP real |
| `Cnpj__UrlBase`, `Cnpj__TimeoutSegundos` | Consulta de CNPJ do organizador (RN24). Padrão: BrasilAPI, 10 s. A API precisa de saída HTTPS para `brasilapi.com.br` |
| `Cnpj__UsarStub` | `true` só em desenvolvimento sem internet: aceita qualquer CNPJ com dígitos válidos como ativo. **Nunca em produção** |
| `Gateway__Asaas__ApiKey`, `Gateway__Asaas__WebhookToken` | Gateway de pagamento (quando houver adaptador real; plano em [pagamentos-asaas.md](.claude/planning/pagamentos-asaas.md)) |
| `Cors__OrigemFrontend` | Origem permitida do frontend |

## Endpoints implementados

| Grupo | Endpoints |
|---|---|
| Autenticação | `POST /api/v1/auth/cadastro`, `login`, `renovar`, `logout` |
| Conta | `GET/PUT /api/v1/usuarios/me`, `POST /usuarios/me/telefone/codigo`, `POST /usuarios/me/telefone/verificar`, `PUT /usuarios/me/preferencias-sms`, `PUT /usuarios/me/cnpj` (CNPJ do organizador) |
| Eventos | `GET /api/v1/eventos` (mural), `GET /eventos/meus`, `GET/PUT /eventos/{id}`, `POST /eventos`, `POST /eventos/{id}/publicar`, `POST /eventos/{id}/cancelar`, `GET /eventos/{id}/alteracoes` |
| Programação e equipe | `POST/PUT/DELETE /eventos/{id}/atividades[/{atividadeId}]`, `GET/POST /eventos/{id}/colaboradores`, `DELETE /eventos/{id}/colaboradores/{usuarioId}` |
| Inscrições | `POST /eventos/{id}/inscricoes`, `GET /eventos/{id}/inscricoes` (inscritos), `GET /inscricoes/minhas`, `POST /inscricoes/{id}/cancelar`, `POST /eventos/{id}/checkin` |
| Pagamentos | `POST/GET /inscricoes/{id}/pagamento`, `POST /pagamentos/webhook` (gateway), `POST /simulador/pagamentos/{idTransacao}/{aprovar\|recusar\|expirar}` (só demonstração) |
| Interesse e notificações | `POST/DELETE /eventos/{id}/interesse`, `GET /notificacoes`, `PATCH /notificacoes/{id}/lida`, `PATCH /notificacoes/lidas` |
| Denúncias e certificados | `POST /eventos/{id}/denuncias`, `GET /inscricoes/{id}/certificado` (PDF), `GET /certificados/{codigo}/validacao` (público) |
| Infra | `GET /api/v1/saude` |

Documentação interativa completa em `/swagger`. Os 39 endpoints da spec (E01–E39) estão implementados, mais o E40 (CNPJ do organizador, RN24): só quem tem CNPJ ativo cria e publica eventos.

## Estado

Etapas 0–6 concluídas (scaffold, domínio/banco/Swagger, autenticação, eventos, inscrições e pagamentos com gateway stub, interesse, notificações com SMS, denúncias e certificados, fechamento de segurança e cobertura). Frontend no repositório `viteme-web`. Etapa 9a concluída: CNPJ do organizador. Próximo: pagamentos reais no sandbox do Asaas ([plano](.claude/planning/pagamentos-asaas.md)). Guia de publicação: [docs/deploy.md](docs/deploy.md). Pendências abertas: [docs/pendencias-api.md](docs/pendencias-api.md). Ver [roadmap](.claude/planning/roadmap.md).
