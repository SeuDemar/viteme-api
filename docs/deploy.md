# Como publicar a API

Guia para a demonstração e para uma publicação simples (uma instância). A API é um aplicativo ASP.NET Core 10; não há Docker no repositório (decisão da equipe).

## 1. Banco (Supabase PostgreSQL)

1. Crie o projeto no Supabase e copie a conexão **direta** ou do **session pooler (porta 5432)**. Não use o pooler em modo *transaction* (6543): a API usa `SELECT ... FOR UPDATE` dentro de transações.
2. Aplique o esquema: `docs/banco/schema.sql` no SQL Editor **ou** `dotnet ef database update --project Viteme.Infrastructure --startup-project Viteme.Api`.
3. A API não aplica migrations sozinha. **Banco já criado?** Rode só as migrations novas, que estão em `docs/banco/migracoes/` (scripts idempotentes: podem rodar mais de uma vez). Ex.: `20261028224208_CnpjUsuario.sql` (CNPJ do organizador, RN24).

## 2. Variáveis de ambiente

| Variável | Valor |
|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `ConnectionStrings__Default` | `Host=...;Port=5432;Database=postgres;Username=...;Password=...;SSL Mode=Require` |
| `Jwt__Chave` | `openssl rand -base64 48` (≥ 32 caracteres; trocar invalida as sessões) |
| `Cors__OrigemFrontend` | Origem do frontend, ex.: `https://app.exemplo.com` (sem ela, o CORS não libera ninguém) |
| `Proxy__ConfiarEmCabecalhosEncaminhados` | `true` atrás de proxy/CDN (a plataforma de hospedagem), para o limite por IP ver o IP real |
| `Proxy__SaltosConfiaveis` | Quantos proxies acrescentam entradas ao `X-Forwarded-For` (padrão `1`). Com o repasse do Cloudflare Pages na frente do Azure App Service use `2`; senão o limite de login por IP vê o IP da Cloudflare, compartilhado por muitos usuários. Quem chamar a API direto pelo endereço `azurewebsites.net` consegue forjar o IP nesse modo: é o preço de ter o repasse |
| `Certificados__UrlValidacao` | Endereço da página de validação, impresso no PDF |
| `Swagger__Habilitar` | `true` só se quiser a documentação interativa no ar |
| `Gateway__Stub__HabilitarSimulador` | `true` **só na demonstração**; `false` em produção |
| `Cnpj__UsarStub` | `false` (padrão). A API consulta o CNPJ do organizador na BrasilAPI (`brasilapi.com.br`), então precisa de saída HTTPS para lá |

Segredos nunca vão para o repositório. Para a demonstração com o gateway e o SMS simulados, deixe `Sms__LogarConteudo=true` apenas se precisar ler o código de verificação no log.

## 3. Cookie de sessão

O refresh token vai em cookie `HttpOnly; Secure; SameSite=None`, restrito a `/api/v1/auth`. Com isso frontend e API **podem ficar em sites diferentes**, mas é preciso:

- **HTTPS** nos dois (o cookie é `Secure`);
- `Cors__OrigemFrontend` com a origem exata do frontend (CORS com credenciais; nunca `*`);
- o frontend enviando `withCredentials: true` e o cabeçalho `X-Requested-With: viteme-web` em `/auth/renovar` e `/auth/logout`. Sem o cabeçalho, ou com `Origin` diferente de `Cors__OrigemFrontend`, a API responde **403 `REQUISICAO_NAO_PERMITIDA`** (proteção contra CSRF, já que `SameSite=None` envia o cookie também em requisições de outros sites).

Atenção: navegadores que bloqueiam cookies entre sites (Safari, Firefox estrito, Chrome com bloqueio de cookies de terceiros) podem **não enviar** o cookie quando frontend e API estão em sites diferentes, e a sessão não restaura ao recarregar a página. Para a demonstração, o mais seguro é hospedar os dois sob o mesmo domínio registrável (ex.: `app.exemplo.com` e `api.exemplo.com`); o `SameSite=None` também funciona assim. Chamadas de `/auth/renovar` feitas pelo Swagger UI da própria API são recusadas (a origem é a da API, não a do frontend).

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

## 7. Publicar no Azure App Service (plano gratuito) com o frontend na Cloudflare

A API está publicada no App Service **`viteme-api`** (plano gratuito F1, região West US 3). A ligação com o GitHub foi feita pelo **Centro de Implantação** do Azure, que criou o workflow `.github/workflows/main_viteme-api.yml` e os segredos `AZUREAPPSERVICE_*` (login por identidade/OIDC, sem perfil de publicação).

O workflow foi ajustado para:

- publicar **somente depois que o CI passa** em um push na `main` (gatilho `workflow_run`), e também à mão em *Actions > Run workflow*;
- publicar o commit exato que passou no CI e apenas o projeto `Viteme.Api`;
- fazer uma publicação por vez.
- apagar a pasta do app antes de copiar (`clean: true`) e fixar o comando de início (`dotnet Viteme.Api.dll`). Sem isso, sobras de publicações antigas (a primeira, gerada pelo Azure, publicou a solução inteira e deixou `Viteme.Tests.runtimeconfig.json`) fazem o Azure avisar *"Expected to find only one file with extension '.runtimeconfig.json'"* e subir a página padrão dele (`hostingstart.dll`) no lugar da API.

Cada publicação reinicia a API. Se o Azure regravar o arquivo (por exemplo, ao refazer a ligação no portal), reaplique os ajustes.

No App Service defina, no mínimo: `ASPNETCORE_ENVIRONMENT=Production`, `ConnectionStrings__Default`, `Jwt__Chave`, `Cors__OrigemFrontend` (o endereço do site na Cloudflare, sem barra final) `Proxy__ConfiarEmCabecalhosEncaminhados=true` e `Proxy__SaltosConfiaveis=2` (por causa do repasse da Cloudflare). O plano gratuito "dorme" sem uso: abra `/api/v1/saude` alguns minutos antes de uma demonstração.

Roteiro completo, do zero (banco no Supabase, assinatura e App Service no Azure, site no Cloudflare Pages): [`passo-a-passo-publicacao.md`](https://github.com/SeuDemar/viteme-web/blob/main/docs/passo-a-passo-publicacao.md) no repositório `viteme-web`.
