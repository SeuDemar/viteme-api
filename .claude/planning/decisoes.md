# Decisões, desvios e pontos em aberto

## 1. Decisões herdadas do PDF

| Decisão | Motivo (resumo) |
|---|---|
| Arquitetura em camadas (Controller → Service → Repository) | Separação de responsabilidades e testes unitários simples |
| Interfaces para repositórios e serviços externos | Inversão de dependência; fornecedor do gateway/SMS trocável |
| Notificações por interface com várias implementações | Aberto/fechado: novos canais (e-mail, WhatsApp) sem alterar o existente |
| Monólito modular | Equipe e escopo pequenos; uma implantação |
| ASP.NET Core, PostgreSQL, EF Core com migrations | Escolha da equipe; transações, enums nativos, índices parciais, `FOR UPDATE` |
| JWT curto + refresh rotativo | API sem sessão de servidor; reduz impacto de vazamento |
| Papel definido por evento, sem administrador | Qualquer pessoa pode organizar; abuso tratado por denúncias (RN19) |
| Gateway de pagamento genérico atrás de `IGatewayPagamento` | Fornecedor ainda não escolhido na época da spec |

## 2. Decisões tomadas na conversa de inicialização (complementam o PDF)

| # | Decisão | Observação |
|---|---|---|
| D1 | **.NET 10 (LTS)** | A spec não fixava versão. Inicialmente .NET 9; trocado porque o 9 sai de suporte em nov/2026 e o ambiente só instala 8 ou 10 |
| D2 | Solution `Viteme.sln` **na raiz** do repositório | |
| D3 | **Somente API** neste repositório; frontend em repositório/pasta própria, **depois da API** | Entrega 3 exige frontend integrado: etapa 7 do roadmap |
| D4 | Banco: **PostgreSQL no Supabase** | Ver nota de conexão abaixo |
| D5 | **Sem Docker/docker-compose** por enquanto | Não há necessidade de rodar nada local no momento |
| D6 | Gateway de pagamento: **Asaas** | Aceita CPF (sem exigir CNPJ), tem sandbox gratuito, Pix nativo. Implementação real fica **para depois**; começa com stub |
| D7 | **SMS: stub** (grava em log/banco, não envia) | Não existe SMS gratuito de verdade no Brasil; provedor real é pay-per-use (opções: AWS SNS, Zenvia, Comtele, Twilio). Decidir perto da entrega |
| D8 | Ordem de implementação: scaffold → Domain + DbContext + migration inicial → Auth (E01–E09) → Eventos → Inscrições/Pagamentos → resto | Ver [roadmap.md](roadmap.md) |
| D9 | Permitido rodar `dotnet build` e `dotnet test` no ambiente para validar compilação | Sem subir a aplicação nem conectar ao banco |
| D11 | Testes de persistência rodam em **PostgreSQL real via variável `VITEME_TESTES_DB`** (banco temporário criado e removido a cada execução). No CI é um serviço `postgres:16`; localmente pode ser qualquer Postgres. Sem a variável, esses testes são ignorados | Substitui o Testcontainers da spec (mesmo resultado, sem exigir Docker no ambiente). Pode voltar ao Testcontainers depois |
| D12 | `RegraDeNegocioException` mora no **Domain** (entidades lançam a exceção; a API converte em ProblemDetails) | Spec previa só validar nos services; invariantes simples ficam nas entidades |
| D13 | Swagger UI em `/swagger` sobre o OpenAPI nativo (`/openapi/v1.json`); em Desenvolvimento sempre ligado, nos demais ambientes só com `Swagger:Habilitar=true` | Exigido pela Entrega 3 |
| D14 | Validação de DTOs com **DataAnnotations** (+ atributos próprios `SenhaForte` e `TelefoneE164` em `Application/Validacoes`), sem FluentValidation | Atende RN02 e os limites da spec com menos dependências; o 400 sai como ProblemDetails com `errors` por campo em camelCase |
| D15 | Rate limiting nativo do ASP.NET Core: login 5/min e cadastro 10/min por IP; envio de código SMS 5/min por usuário; tudo configurável em `RateLimiting:*` (desligável em testes) | Spec 4.5. Contadores em memória (uma instância) |
| D16 | `Proxy:ConfiarEmCabecalhosEncaminhados=true` liga `X-Forwarded-For/Proto` | Para o rate limiting enxergar o IP real atrás de proxy/CDN (P6) |
| D17 | Refresh token e código de verificação guardados com **HMAC-SHA256** (chave derivada de `Jwt:Chave`), não SHA-256 puro | Código de 6 dígitos tem só 10^6 possibilidades; sem a chave um vazamento do banco não permite reverter |
| D18 | O **preço** só pode mudar se o evento não tiver nenhuma inscrição ativa (confirmada ou pendente de pagamento) | A spec (RN13) diz "paga ou pendente"; ficou mais estrito para não cobrar de quem se inscreveu em evento gratuito. Atende CT41 |
| D19 | Evento **OCULTO** é tratado como publicado nas regras de alteração (RN13): mudar título, data ou local exige motivo e avisa os inscritos | Inscritos continuam vendo o evento oculto (RN19) |
| D20 | Mudar a **programação** de evento publicado (E18, E19, E20) exige motivo, gera histórico (campo `programacao`) e avisa os inscritos | RN13 cita programação, mas a tabela do E18 não listava `motivoAlteracao`; adicionado como campo opcional (obrigatório se publicado) |
| D21 | Enums saem no JSON como texto em maiúsculas com sublinhado (`PENDENTE_PAGAMENTO`, `ORGANIZADOR`) e entram do mesmo jeito | Mesmo formato dos enums do banco e dos exemplos da spec |
| D22 | Filtros `dataDe`/`dataAte` do mural comparam a data de início do evento por **dia em UTC**, com `dataAte` inclusivo | Simples; ver P26 para o fuso de Brasília |
| D23 | Políticas por papel respondem **404** (não 403) quando o usuário não pode nem ver o evento (rascunho de outra pessoa, ou evento oculto sem inscrição) e **403** quando pode ver mas não tem o papel | Spec: "404 para quem não pode ver (não revela a existência de rascunhos)" |
| D10 | Desenvolvimento na branch `claude/wizardly-knuth-9ob62s`; sem PR até pedido explícito | |

### Nota sobre Supabase + Npgsql

- O pooler do Supabase em **modo transaction (porta 6543)** não suporta prepared statements por sessão, que o Npgsql usa por padrão. Usar **conexão direta ou session pooler (porta 5432)**, principalmente para migrations.
- Connection string e segredos (chave JWT, credenciais Asaas) vão em **variáveis de ambiente / user-secrets**, nunca no repositório.
- `SELECT ... FOR UPDATE` (RI10), `xmin` como token de concorrência (RI11), enums nativos e índices parciais funcionam no PostgreSQL do Supabase.
- Testes de integração com Testcontainers precisam de Docker; ficam para quando houver ambiente que o suporte (D5 não impede, só adia).

## 3. Escopo

**Dentro desta versão:** os 39 endpoints, as 23 regras de negócio, as 14 entidades, tarefas em segundo plano, testes unitários e de integração.

**Fora desta versão (spec, seção 1.7):** cupons e benefícios; relatórios além da lista de inscritos; notificações por e-mail/WhatsApp (entram como novos canais); aplicativo nativo; exclusão de conta pelo próprio usuário (será por anonimização); escolha do provedor de SMS.

**Frontend:** adiado só em ordem de implementação; a Entrega 3 exige frontend integrado, então volta ao escopo na etapa 7 ([entrega-3.md](entrega-3.md)). Não transcrito nestes documentos: seção 5 do PDF (tecnologias, telas T01–T12, navegação, fluxos, formulários e interações com a API) e os casos CT58–CT63. Consultar o PDF original quando o frontend for retomado.

## 4. Pontos em aberto / riscos a verificar

Itens que a spec não resolve ou que dependem de confirmação antes da implementação real da etapa correspondente.

1. **Asaas exige CPF/CNPJ do pagador?** A tabela `usuario` não tem CPF. Se a API do Asaas exigir CPF para criar cliente/cobrança, será preciso decidir: coletar CPF (novo campo, LGPD, validação) ou usar outro fluxo. Verificar na documentação do Asaas antes de implementar o adaptador. Isso **não bloqueia** o stub.
2. **Validação de assinatura do webhook.** A spec prevê HMAC (E31, RN23). O Asaas autentica webhooks por **token enviado em cabeçalho**, não necessariamente HMAC. O contrato `ValidarAssinaturaWebhook` em `IGatewayPagamento` deve abstrair isso; o adaptador Asaas implementa o mecanismo real. Confirmar na documentação.
3. **Geração de PDF do certificado (E38).** A spec não define biblioteca. Candidata: QuestPDF (verificar licença para uso acadêmico/comunitário). Decidir na etapa de certificados.
4. **Provedor de SMS real.** Pendente (D7). Se for AWS SNS, atenção ao sandbox, ao registro de sender ID no Brasil e a limites de gasto.
5. **Fuso horário.** Datas em UTC no banco e na API; conversão para Brasília é só na exibição (frontend). Regras com "24 horas antes" etc. usam `IRelogio` em UTC.
6. **Rate limiting por IP atrás de proxy.** Se a API rodar atrás de proxy/CDN, configurar `ForwardedHeaders` para o IP correto, senão o limite por IP e o bloqueio de RN03 ficam ineficazes.
7. **Cookie do refresh token e domínios.** `SameSite=Strict` exige frontend e API no mesmo site (ex.: `viteme.app` e `api.viteme.app`). Relevante só quando o frontend existir; manter a configuração parametrizável.
8. **Coerência da spec.** Verificações feitas na leitura: 14 entidades listadas e detalhadas; 39 endpoints; RN01–RN23 todas na matriz de rastreabilidade; `envio_sms.tentativas ≤ 4` é consistente com RN16 (1 envio + até 3 novas tentativas). Nenhuma contradição encontrada; revisar se surgir dúvida durante a implementação.

## 5. Convenções de trabalho

- Código C# em `PascalCase`; tabelas e colunas em `snake_case` em português (mapeadas pelo EF Core); JSON em `camelCase`.
- Nomes de domínio em português, como na spec (`Inscricao`, `Evento`, `IGatewayPagamento`).
- Toda regra de negócio nova ou alterada atualiza [api/regras-de-negocio.md](api/regras-de-negocio.md) e a matriz de rastreabilidade em [testes.md](testes.md).

## 6. Acréscimos em relação à spec (códigos e comportamentos novos)

Surgiram ao implementar o domínio. Manter a spec e o código alinhados: se a equipe aprovar, refletir no PDF.

| Item | Onde | Motivo |
|---|---|---|
| `EVENTO_JA_PUBLICADO` (409) | `Evento.Publicar` | A spec só define o fluxo RASCUNHO/OCULTO → PUBLICADO; publicar de novo precisava de um erro |
| `PERIODO_INVALIDO` (400) | `Evento.ValidarPeriodos`, `AtividadeProgramacao` | Rede de segurança no domínio; o validador de DTO deve responder 400 com erro por campo antes (CT15, CT16) |
| `DESCRICAO_OBRIGATORIA` (400) | `Denuncia` | Motivo `OUTRO` exige descrição (também há CHECK no banco) |
| `VALOR_INVALIDO` (400), `PAGAMENTO_NAO_PENDENTE/APROVADO` (409), `INSCRICAO_NAO_PENDENTE/ATIVA` (409) | `Pagamento`, `Inscricao` | Transições de estado inválidas |
| `ERRO_INTERNO` (500) | Middleware de erros | Código estável para erros inesperados |
| Aprovação tardia de pagamento expirado é registrada como APROVADO | `Pagamento.Aprovar` | RN23: depois o service solicita o estorno automático |
| `EnvioSms` permanece PENDENTE entre as 3 novas tentativas e só vira FALHOU na 4.ª falha | `EnvioSms` | Permite à tarefa de 1 minuto saber o que reenviar (RN16) |
| `NAO_AUTENTICADO` (401) | `OnChallenge` do JWT | Token ausente, inválido ou expirado em endpoint protegido, com ProblemDetails |
| `DADOS_INVALIDOS` (400) | Validação de DTOs | Código estável para erros por campo |
| `LIMITE_EXCEDIDO` (429) | Rate limiting | Código estável para 429 |
| `SMS_INDISPONIVEL` (502) | `UsuarioService.EnviarCodigoTelefoneAsync` | Falha do provedor ao enviar o código (a spec não previa) |
| `USUARIO_NAO_ENCONTRADO` (401) | `UsuarioService` | Token válido de usuário que não existe mais |
| `VERSAO_OBRIGATORIA` (428) | E13 sem cabeçalho `If-Match` | A spec só define 412 para versão desatualizada |
| `VERSAO_INVALIDA` (400) | E13 com `If-Match` que não é um ETag | Entrada malformada |
| `ACESSO_NEGADO` (403) | Políticas por papel | Código estável para 403 |
| `ATIVIDADE_NAO_ENCONTRADA` (404), `COLABORADOR_NAO_ENCONTRADO` (404) | E19/E20, E23 | Recursos filhos inexistentes |
