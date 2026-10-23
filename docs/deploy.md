# Como publicar a API

Guia para a demonstração e para uma publicação simples (uma instância). A API é um aplicativo ASP.NET Core 10; não há Docker no repositório (decisão da equipe).

## 1. Banco (Supabase PostgreSQL)

1. Crie o projeto no Supabase e copie a conexão **direta** ou do **session pooler (porta 5432)**. Não use o pooler em modo *transaction* (6543): a API usa `SELECT ... FOR UPDATE` dentro de transações.
2. Aplique o esquema: `docs/banco/schema.sql` no SQL Editor **ou** `dotnet ef database update --project Viteme.Infrastructure --startup-project Viteme.Api`.
3. A API não aplica migrations sozinha.

## 2. Variáveis de ambiente

| Variável | Valor |
|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `ConnectionStrings__Default` | `Host=...;Port=5432;Database=postgres;Username=...;Password=...;SSL Mode=Require` |
| `Jwt__Chave` | `openssl rand -base64 48` (≥ 32 caracteres; trocar invalida as sessões) |
| `Cors__OrigemFrontend` | Origem do frontend, ex.: `https://app.exemplo.com` (sem ela, o CORS não libera ninguém) |
| `Proxy__ConfiarEmCabecalhosEncaminhados` | `true` atrás de proxy/CDN (a plataforma de hospedagem), para o limite por IP ver o IP real |
| `Certificados__UrlValidacao` | Endereço da página de validação, impresso no PDF |
| `Swagger__Habilitar` | `true` só se quiser a documentação interativa no ar |
| `Gateway__Stub__HabilitarSimulador` | `true` **só na demonstração**; `false` em produção |

Segredos nunca vão para o repositório. Para a demonstração com o gateway e o SMS simulados, deixe `Sms__LogarConteudo=true` apenas se precisar ler o código de verificação no log.

## 3. Cookie de sessão

O refresh token vai em cookie `HttpOnly; Secure; SameSite=Strict`. Por isso frontend e API precisam estar no **mesmo site** (ex.: `app.exemplo.com` e `api.exemplo.com`) e em HTTPS. Em domínios diferentes o navegador não envia o cookie e a sessão não é renovada.

## 4. Execução

```bash
dotnet publish Viteme.Api -c Release -o out
cd out && dotnet Viteme.Api.dll   # escuta em ASPNETCORE_URLS
```

Verifique `GET /api/v1/saude` (retorna 200 com o banco acessível).

## 5. Tarefas em segundo plano

A própria API roda encerramento de eventos (15 min), expiração de reservas e estornos (1 min), SMS (1 min) e lembretes (15 min). Use **uma única instância**: com várias, as tarefas rodam em duplicidade (são idempotentes, mas gastam tentativas de SMS e estornos) e o rate limiting em memória fica por instância (pendência P16).

## 6. Antes de publicar de verdade (fora do stub)

- Adaptador do **Asaas** (pagamento) no lugar do gateway stub: resolver CPF do pagador (P1), autenticação do webhook (P2) e idempotência de estorno (P30).
- Provedor de **SMS** real (P4).
- Desligar o simulador de pagamento (P32).
