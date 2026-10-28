# Autenticação e segurança

## 1. Cadastro e login

- Senha validada por RN02 e armazenada **somente como hash** (PBKDF2 via `PasswordHasher` do ASP.NET Core: sal aleatório e várias iterações).
- Fluxo do login (E02):
  1. Usuário informa e-mail e senha.
  2. API verifica bloqueio (RN03) → se bloqueada, **423**.
  3. Compara a senha com o hash. Erro: incrementa `tentativas_login`, bloqueia na 5ª tentativa e responde **401** com mensagem genérica (igual para e-mail inexistente).
  4. Sucesso: zera tentativas e emite token de acesso (JWT) e token de renovação.
  5. Token de acesso volta no corpo e fica **em memória** no frontend; token de renovação vai em cookie `HttpOnly; Secure; SameSite=None`, restrito à rota `/api/v1/auth` (ver D47).
  6. Ao expirar o acesso, o frontend chama E03: recebe novo par e o refresh anterior é **revogado** (rotação).
  7. Ao abrir/recarregar o site, o frontend chama E03 para restaurar a sessão.
  8. Logout (E04): revoga o refresh e remove o cookie.

## 2. Tokens

| Item | Definição |
|---|---|
| Algoritmo | HS256 (HMAC-SHA256), chave secreta ≥ 256 bits em **variável de ambiente**, fora do código |
| Validade | Acesso: **15 min**. Renovação: **7 dias**, renovado a cada uso |
| Claims do acesso | `sub` (id do usuário), `name`, `email`, `jti`, `iat`, `exp`, `iss`, `aud`. **Sem papéis** (o papel depende do evento e é verificado a cada requisição) |
| Validações na API | Assinatura, emissor (`iss`), audiência (`aud`), expiração (tolerância de 30 s) e algoritmo esperado |
| Refresh token | Valor aleatório de 64 bytes; no banco só o **hash** (`token_renovacao`). Reuso de token já revogado ⇒ **revoga todos os tokens do usuário** (indício de roubo) |
| Armazenamento no frontend | Acesso em memória (não em localStorage, para reduzir risco de XSS); refresh só no cookie HttpOnly |
| Domínios/cookies | Cookie `SameSite=None; Secure` (D47): frontend e API podem estar em sites diferentes, sempre em HTTPS. E02, E03, E04 usam `withCredentials`; CORS com `AllowCredentials` e origem do frontend **explícita (nunca `*`)**. Divergência da spec (que previa `Strict` e mesmo site) |

## 3. Autorização e controle de acesso

Dois níveis:

1. **Autenticação:** endpoints com `[Authorize]` exigem token válido.
2. **Papel no evento:** políticas do ASP.NET Core (`OrganizadorDoEvento`, `OrganizadorOuColaborador`) cujos handlers **consultam o banco** para saber se o usuário é organizador/colaborador do evento da rota.

Recursos pessoais (inscrições, notificações, certificados) são **sempre filtrados pelo usuário do token**; o id da rota nunca basta sozinho (retornar 404 para recurso alheio).

### Matriz de acesso

O papel (participante, colaborador, organizador) é **relativo a um evento**. A mesma pessoa pode ser organizadora de um evento e participante de outro.

| Funcionalidade | Visitante | Participante | Colaborador | Organizador |
|---|---|---|---|---|
| Ver mural, detalhes e alterações de eventos publicados | Sim | Sim | Sim | Sim |
| Ver evento em rascunho | Não | Não | Sim | Sim |
| Ver evento oculto por denúncias | Não | Só se inscrito | Sim | Sim |
| Criar evento | Não | Sim | Sim | Sim |
| Inscrever-se, pagar, cancelar a própria inscrição | Não | Sim | Não | Não |
| Acompanhar evento e receber notificações | Não | Sim | Sim | Sim |
| Denunciar evento | Não | Sim | Não | Não |
| Baixar o próprio certificado | Não | Sim | Não | Não |
| Validar certificado | Sim | Sim | Sim | Sim |
| Ver inscritos e fazer check-in | Não | Não | Sim | Sim |
| Editar, publicar e cancelar o evento | Não | Não | Não | Sim |
| Gerenciar programação e colaboradores | Não | Não | Não | Sim |

## 4. Verificação de telefone e consentimento para SMS (LGPD)

O SMS só é enviado com **consentimento explícito e telefone confirmado**:

1. Usuário informa o telefone (cadastro ou perfil) e pede o código (E07); código de 6 dígitos gerado por gerador criptograficamente seguro e armazenado **só como hash**.
2. Confirma o código (E08), respeitando validade (10 min), limite de tentativas (5) e intervalo entre envios (60 s) — RN18.
3. Com telefone verificado, ativa o recebimento (E09); data do aceite em `aceite_sms_em`; o aceite pode ser **revogado a qualquer momento**.
4. Trocar o telefone desfaz a verificação e o aceite; o telefone **não aparece** para organizadores nem colaboradores.

> Enquanto o SMS for **stub**, o fluxo todo existe e é testável; o código só é gravado/logado em vez de enviado. Cuidado para o stub **não logar o código em ambientes de produção**.

## 4.1 CNPJ do organizador (RN24)

- CNPJ é dado público de pessoa jurídica (não é dado pessoal sensível); a razão social vem da própria Receita. Guardamos só CNPJ, razão social e data da verificação.
- A consulta externa (BrasilAPI) envia apenas o CNPJ. Limite de 5 consultas por minuto por usuário (E40) para não abusar do serviço gratuito.
- Sem verificação não há liberação: se a consulta cair, a API responde 502 e o usuário tenta de novo.

## 5. Riscos e medidas

| Risco | Medida |
|---|---|
| Senha fraca ou vazada | Política RN02, hash PBKDF2, bloqueio temporário (RN03), mensagens de erro genéricas no login |
| Bloqueio provocado por terceiros | Bloqueio curto (15 min) + rate limiting por IP |
| Força bruta e abuso | Rate limiting do ASP.NET Core por IP e por usuário: login (5/min), envio de código (RN18), denúncias e validação de certificado |
| Acesso indevido a dados de outro usuário | Autorização por política em todo endpoint protegido; recursos pessoais filtrados pelo `sub`; 404 para alheios |
| Injeção de SQL | Somente EF Core com parâmetros; nenhuma concatenação de SQL com dados do usuário |
| XSS | Descrição do evento é **texto puro** (sem HTML); frontend escapa por padrão; Content-Security-Policy no frontend |
| CSRF | API usa cabeçalho `Authorization` (não cookies) para autenticar. O cookie de renovação é `SameSite=None` e restrito à rota de auth; E03 e E04, os únicos que dependem só do cookie, exigem o cabeçalho `X-Requested-With: viteme-web` (força preflight de CORS, que só a origem do frontend passa) e conferem `Origin` contra `Cors:OrigemFrontend` → 403 `REQUISICAO_NAO_PERMITIDA` (D47) |
| Interceptação | HTTPS obrigatório em produção, com HSTS; CORS só para o domínio do frontend |
| Webhook falsificado | Validação de assinatura/token do gateway, conferência do valor com o registrado, processamento idempotente (RN23) |
| Inconsistência de vagas e pagamentos | Transações com bloqueio de linha (RI10), concorrência otimista (RI11), estorno automático de pagamentos fora de contexto (RN23) |
| Entrada malformada | Validação de **todos** os DTOs na API (tamanho, formato, faixa), mesmo que o frontend valide |
| Exposição de informação | Erros 500 sem detalhes internos; logs **sem** senhas, tokens, códigos ou telefones completos; segredos (chave JWT, credenciais do gateway e do SMS, connection string) em variáveis de ambiente |
| Privacidade (LGPD) | Coleta mínima, consentimento de SMS registrado, revogação do aceite, exclusão de conta por anonimização em versão futura |

## 5.1 Webhook do gateway (E31)

- Sem JWT e sem rate limiting; autenticidade pela **assinatura do corpo exato**. No stub: HMAC-SHA256 (hex, comparação em tempo constante) no cabeçalho `X-Viteme-Assinatura`; sem segredo configurado o stub gera um efêmero, então nada de fora consegue assinar.
- Processamento idempotente, valor conferido contra o registrado, corpo limitado a 64 KB e transações protegidas por `xmin` (D26).
- O **simulador de pagamento** (D25) é só para demonstração: exige login, só age sobre cobranças do próprio usuário e fica desligado por padrão.

## 6. Checklist de segredos (nunca no repositório)

- `ConnectionStrings__Default` (Supabase; conexão direta ou session pooler, porta 5432)
- `Jwt__Chave`, `Jwt__Emissor`, `Jwt__Audiencia`
- `Gateway__Stub__SegredoWebhook` (opcional) e, quando houver adaptador real, `Gateway__Asaas__ApiKey` e `Gateway__Asaas__WebhookToken`
- `Sms__*` (quando houver provedor real)
- `Cors__OrigemFrontend`

Em desenvolvimento: `dotnet user-secrets`. Em produção: variáveis de ambiente do host.
