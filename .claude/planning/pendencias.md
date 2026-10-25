# Pendências técnicas

Nenhuma bloqueia o desenvolvimento. Cada uma tem a etapa em que precisa ser resolvida ([roadmap.md](roadmap.md)). Contexto em [decisoes.md](decisoes.md), seção 4.

| # | Pendência | Quem decide | Resolver até | Status |
|---|---|---|---|---|
| P1 | Asaas exige CPF/CNPJ do pagador? `usuario` não tem CPF. Se exigir: novo campo, validação e implicações de LGPD | Equipe | Antes do adaptador Asaas real (pós-etapa 4) | Aberta |
| P2 | Webhook do Asaas autentica por token em cabeçalho, não necessariamente HMAC. Confirmar na documentação; `ValidarAssinaturaWebhook` já abstrai | Dev | Antes do adaptador Asaas real | Aberta |
| P3 | ~~Biblioteca de PDF do certificado~~ — escritor próprio (D34), sem dependência | Equipe | Etapa 5 | **Resolvida** |
| P4 | Provedor de SMS real (pay-per-use; opções AWS SNS, Zenvia, Comtele, Twilio). Se AWS SNS: sandbox, registro de sender ID no Brasil, orçamento/limite de gasto | Equipe | Etapa 6 (opcional) | Aberta |
| P5 | Supabase: usar conexão direta ou session pooler (porta 5432); nunca o pooler transaction (6543) por causa de prepared statements do Npgsql | Dev | Etapa 1 (migration) | Registrada |
| P6 | Rate limiting por IP atrás de proxy/CDN exige `ForwardedHeaders`. Implementado por configuração (`Proxy:ConfiarEmCabecalhosEncaminhados`, D16, e `Proxy:SaltosConfiaveis`, D49); **ativar na hospedagem** (`true` e `2` com o repasse da Cloudflare) e restringir proxies confiáveis se possível | Dev/Equipe | Na hospedagem (P11) | Parcial |
| P7 | Testes de integração: resolvido com PostgreSQL via `VITEME_TESTES_DB` (serviço no CI, ver D11). Testcontainers fica opcional | Dev | Etapa 0 (CI) | Resolvida |
| P8 | ~~`SameSite=Strict` exige frontend e API no mesmo site~~ — cookie passou a `SameSite=None` com proteção CSRF (D47); origem configurável por `Cors:OrigemFrontend` | Dev | — | Resolvida |
| P9 | SDK .NET no ambiente de desenvolvimento: .NET 9 indisponível (não está no apt; site da Microsoft bloqueado pela rede). Decidido usar .NET 10 LTS, instalado via apt (`dotnet-sdk-10.0`) | Dev | Etapa 0 | Resolvida |
| P10 | Frontend é exigido pela Entrega 3 (antes adiado). Definir prazo, responsável e stack | Equipe | Antes da etapa 7 | Aberta |
| P11 | Hospedagem/demonstração: onde publicar API, frontend (HTTPS; mesmo site não é mais exigido, D47, mas é o mais seguro contra bloqueio de cookies entre sites) e banco, e acesso da banca ao Supabase | Equipe | Antes da etapa 7 | Aberta |
| P12 | Autoria no Git: commits atuais saem como "Claude"; cada integrante precisa commitar com identidade própria (branches + PRs) | Equipe | Desde já | Aberta |
| P13 | Swagger UI obrigatório | Dev | Etapa 1 | Resolvida (`/swagger`) |
| P14 | Pagamento/SMS em stub na demonstração: declarar na apresentação ou usar sandbox do Asaas (torna P1/P2 bloqueantes) | Equipe | Antes da demonstração | Aberta |
| P15 | ~~Rotação do refresh token sem proteção contra corrida~~ — a revogação agora é uma instrução condicional (`WHERE revogado_em IS NULL`): só uma de várias renovações simultâneas emite o novo par (D43) | Dev | Etapa 6 | **Resolvida** |
| P16 | Rate limiting em memória: com mais de uma instância da API os limites são por instância. Aceitável para um monólito de instância única | Equipe | Hospedagem | Aceita |
| P17 | O token de acesso (JWT, 15 min) continua válido depois do logout, por ser stateless. Aceito pela spec; refresh é revogado | Equipe | — | Aceita |
| P18 | ~~JWT validado pelo relógio do sistema~~ — validação de validade usa `IRelogio` (D44); a expiração tem teste automático | Dev | Etapa 6 | **Resolvida** |
| P19 | `POST /auth/cadastro` revela se um e-mail já existe (409, exigido pela spec). Mitigado só pelo rate limiting por IP | Equipe | — | Aceita |
| P20 | ~~SMS bombing~~ — além de 60 s e 5/min por usuário, há limite de 5 códigos por hora **por telefone**, somando todas as contas (D45) | Dev | Antes do SMS real | **Resolvida** |
| P21 | ~~Cobertura de `Application/Services`~~ — medida: **93,4%** (meta 70%). Ver [cobertura](../../docs/evidencias/cobertura-etapa-6.txt) | Dev | Etapa 6 | **Resolvida** |
| P22 | Ambiente de desenvolvimento: o PostgreSQL local usado nos testes não sobe sozinho em sessão nova (`pg_ctlcluster 16 main start`) e `VITEME_TESTES_DB` precisa estar definida | Dev | — | Informativa |
| P23 | Estorno ao cancelar evento (RN14) | Dev | Etapa 4 | Resolvida (D30; CT45) |
| P24 | ~~RN19: republicar oculto uma vez e cancelamento automático~~ — implementado (D39) | Dev | Etapa 5 | **Resolvida** |
| P25 | Avisos de alteração e cancelamento são gravados um a um na mesma transação; para eventos com muitos inscritos considerar lotes ou fila | Dev | Se escalar | Aceita |
| P26 | Filtros de data do mural usam o dia em UTC, não o fuso de Brasília (UTC−3); eventos à noite podem cair no dia seguinte | Equipe | Etapa 7 (frontend) | Aberta |
| P27 | ~~Canal SMS das notificações~~ — implementado (D35, D36) | Dev | Etapa 5 | **Resolvida** |
| P28 | E16, E17 e E21 retornam listas sem paginação (a spec não pede). Reavaliar se o volume crescer | Dev | — | Aceita |
| P29 | **Reconciliação com o gateway:** hoje a situação do pagamento só muda pelo webhook. Se um webhook se perder, o participante pagou e a reserva expira (o estorno só ocorre quando o aviso chegar). O Asaas reenvia webhooks, mas vale usar `ConsultarCobrancaAsync` para conferir cobranças pendentes perto do vencimento | Dev | Antes do adaptador Asaas real | Aberta |
| P30 | O estorno é pedido ao gateway **antes** de gravar `ESTORNADO`. Se o `SaveChanges` falhar depois do gateway aceitar, o próximo ciclo pede de novo: o adaptador real deve enviar chave de idempotência (ex.: id do pagamento) | Dev | Adaptador Asaas | Aberta |
| P31 | Enquanto o estorno estiver pendente o pagamento aparece como APROVADO com inscrição CANCELADA/EXPIRADA; a tela deve mostrar "estorno em andamento" nesse caso | Equipe | Etapa 7 (frontend) | Aberta |
| P32 | O **simulador de pagamento** (D25) nunca pode ficar ligado em produção: `Gateway__Stub__HabilitarSimulador=false`. Remover quando o Asaas entrar | Dev | Antes de publicar | Aberta |
| P33 | O participante não é avisado quando a reserva expira (RN11): hoje só o mural e "Minhas inscrições" refletem | Equipe | Opcional | Aberta |
| P34 | Pagamentos da mesma inscrição criados no mesmo instante teriam ordem indefinida em "mais recente"; na prática há segundos entre tentativas. Considerar `numero` sequencial | Dev | — | Aceita |
| P35 | Aprovação por webhook chegando com a reserva já vencida é estornada automaticamente (RN23), mesmo se ainda houver vaga. Regra da spec; pode frustrar o participante que pagou no limite | Equipe | — | Aceita |
| P36 | O PDF usa só as fontes padrão (Helvetica, WinAnsi): nomes com caracteres fora do Latin-1 (ex.: alfabeto cirílico, emojis) saem como `?` | Equipe | Opcional | Aceita |
| P37 | O lembrete de início só vai para inscrições CONFIRMADAS e o do fim das inscrições só para interessados sem inscrição; quem tem inscrição pendente de pagamento não é lembrado de pagar | Equipe | Opcional | Aberta |
| P38 | O link de validação no PDF vem de `Certificados:UrlValidacao` (padrão `https://viteme.app/validar`). Definir o domínio real e criar a tela de validação no frontend (E39 é público) | Equipe | Etapa 7 | Aberta |
| P39 | O SMS stub não envia de verdade: o fluxo (fila, tentativas, descarte) está pronto e testado; falta escolher o provedor (P4) | Equipe | Etapa 6 | Aberta |
| P40 | Reuso de refresh token já revogado revoga **todas** as sessões do usuário (regra da spec). Duas abas renovando no mesmo instante podem derrubar a sessão; o frontend deve renovar uma vez e compartilhar o resultado entre abas | Equipe | Etapa 7 | Aberta |
| P41 | `Infrastructure/Tarefas` tem pouca cobertura (27,7%): os workers são desligados nos testes. A lógica executada por eles tem 90%+; o laço de repetição em si é simples | Dev | — | Aceita |
| P42 | O CSP (Content-Security-Policy) é responsabilidade do frontend/host estático; a API envia `nosniff`, `X-Frame-Options`, `Referrer-Policy` e `Cache-Control: no-store` | Equipe | Etapa 7 | Aberta |
