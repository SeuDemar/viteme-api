# Pendências técnicas

Nenhuma bloqueia o desenvolvimento. Cada uma tem a etapa em que precisa ser resolvida ([roadmap.md](roadmap.md)). Contexto em [decisoes.md](decisoes.md), seção 4.

| # | Pendência | Quem decide | Resolver até | Status |
|---|---|---|---|---|
| P1 | Asaas exige CPF/CNPJ do pagador? `usuario` não tem CPF. Se exigir: novo campo, validação e implicações de LGPD | Equipe | Antes do adaptador Asaas real (pós-etapa 4) | Aberta |
| P2 | Webhook do Asaas autentica por token em cabeçalho, não necessariamente HMAC. Confirmar na documentação; `ValidarAssinaturaWebhook` já abstrai | Dev | Antes do adaptador Asaas real | Aberta |
| P3 | Biblioteca de PDF do certificado (candidata: QuestPDF; checar licença) | Equipe | Etapa 5 | Aberta |
| P4 | Provedor de SMS real (pay-per-use; opções AWS SNS, Zenvia, Comtele, Twilio). Se AWS SNS: sandbox, registro de sender ID no Brasil, orçamento/limite de gasto | Equipe | Etapa 6 (opcional) | Aberta |
| P5 | Supabase: usar conexão direta ou session pooler (porta 5432); nunca o pooler transaction (6543) por causa de prepared statements do Npgsql | Dev | Etapa 1 (migration) | Registrada |
| P6 | Rate limiting por IP atrás de proxy/CDN exige `ForwardedHeaders` configurado, senão limite e bloqueio de login não funcionam | Dev | Etapa 2 | Aberta |
| P7 | Testes de integração (Testcontainers) precisam de Docker; sem Docker rodam só os unitários. Precisa de CI com Docker para cumprir o critério de aceite | Equipe | Etapa 0 (CI) e etapa 4 (CT26) | Aberta |
| P8 | `SameSite=Strict` exige frontend e API no mesmo site; manter origem/domínio do cookie parametrizáveis | Dev | Quando o frontend existir | Adiada |
| P9 | SDK .NET no ambiente de desenvolvimento: .NET 9 indisponível (não está no apt; site da Microsoft bloqueado pela rede). Decidido usar .NET 10 LTS, instalado via apt (`dotnet-sdk-10.0`) | Dev | Etapa 0 | Resolvida |
| P10 | Frontend é exigido pela Entrega 3 (antes adiado). Definir prazo, responsável e stack | Equipe | Antes da etapa 7 | Aberta |
| P11 | Hospedagem/demonstração: API, frontend e banco no mesmo site (cookie `SameSite=Strict`) e acesso da banca ao Supabase | Equipe | Antes da etapa 7 | Aberta |
| P12 | Autoria no Git: commits atuais saem como "Claude"; cada integrante precisa commitar com identidade própria (branches + PRs) | Equipe | Desde já | Aberta |
| P13 | Swagger UI obrigatório (hoje só OpenAPI JSON) | Dev | Etapa 2 | Aberta |
| P14 | Pagamento/SMS em stub na demonstração: declarar na apresentação ou usar sandbox do Asaas (torna P1/P2 bloqueantes) | Equipe | Antes da demonstração | Aberta |
