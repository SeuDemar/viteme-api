# Roadmap de implementação (API)

Ordem combinada: scaffold → Domain + DbContext + migration inicial → Auth (E01–E09) → Eventos → Inscrições/Pagamentos → resto.

Premissas: .NET 10 (LTS), `Viteme.sln` na raiz, PostgreSQL (Supabase), gateway e SMS em **stub**, sem Docker/compose. Frontend: etapa 7 (exigido pela Entrega 3, ver [entrega-3.md](entrega-3.md)). Cada etapa fecha com `dotnet build` e `dotnet test` verdes (sem subir a aplicação nem conectar ao banco).

## Etapa 0 — Scaffold

- `Viteme.sln` com `Viteme.Domain`, `Viteme.Application`, `Viteme.Infrastructure`, `Viteme.Api`, `Viteme.Tests` e as referências da regra de dependência.
- `global.json`/`Directory.Build.props` (net10.0, nullable, warnings como erros onde fizer sentido), `.editorconfig`, `.gitignore` para .NET.
- Pacotes base: EF Core + Npgsql, JWT Bearer, OpenAPI/Swagger, xUnit + NSubstitute (ou Moq), FluentValidation (a decidir).
- Estrutura de pastas do [arquitetura.md](arquitetura.md).
- `Program.cs` mínimo: DI, ProblemDetails, middleware de exceções, versionamento `/api/v1`, `GET /api/v1/saude` (health).
- Configuração por variáveis de ambiente/user-secrets (ver checklist em [seguranca.md](seguranca.md)).
- README do repositório com como rodar e como configurar segredos.
- CI (GitHub Actions): build + testes unitários a cada push.

**Pronto quando:** compila, 1 teste de exemplo passa, CI verde.

> **Concluída.** `dotnet build` sem warnings e 3 testes passando (health check e middleware de erros). Ficaram para depois: FluentValidation (decidir na etapa 2) e Swagger UI (por ora só o OpenAPI nativo em desenvolvimento).

## Etapa 1 — Domain + DbContext + migration inicial

- 14 entidades e 6 enums (com comportamento: `Evento.Publicar()`, `Inscricao.Confirmar()` etc.).
- `VitemeDbContext`, configurações Fluent API (snake_case, enums nativos, `xmin`, índices e CHECKs de [banco/modelo.md](banco/modelo.md)).
- Migration inicial.
- `IUnitOfWork` e repositories base; `IRelogio` (+ implementação de sistema).
- Testes unitários das entidades (invariantes).

**Pronto quando:** migration gerada e revisada (SQL conferido contra RI01–RI12); testes de entidade passando.

## Etapa 2 — Autenticação e conta (E01–E09)

- `AuthService`, `ITokenService` (JWT HS256, refresh com hash, rotação e detecção de reuso), PasswordHasher.
- Cookie de refresh (`HttpOnly; Secure; SameSite=Strict`, path `/api/v1/auth`), CORS com credenciais.
- Bloqueio de login (RN03), rate limiting (login 5/min), ProblemDetails com `codigo`.
- **Swagger UI** (exigido pela Entrega 3): esquema Bearer, descrição de erros, acessível no ambiente da demonstração.
- Decidir biblioteca de validação (FluentValidation ou DataAnnotations).
- `UsuarioService`: perfil, telefone, código de verificação (hash, 10 min, 5 tentativas, 60 s entre envios), aceite de SMS (RN18).
- `INotificationSender` + **stub de SMS**.
- Testes: CT01–CT13.

**Pronto quando:** CT01–CT13 passando; revisão de segurança da seção 1–2 de [seguranca.md](seguranca.md).

## Etapa 3 — Eventos e programação (E10–E23)

- `EventoService`: criação, edição com `If-Match`/ETag, publicar, cancelar (sem estornos reais ainda), listagem com filtros e paginação, "meus eventos", histórico de alterações.
- Atividades (E18–E20) e colaboradores (E21–E23).
- Políticas `OrganizadorDoEvento` e `OrganizadorOuColaborador`.
- Notificação por site (`NotificacaoSiteSender`) e esqueleto de `NotificacaoService` (RN16).
- Tarefa de encerramento (RN22).
- Testes: CT14–CT22, CT47, CT48, CT39–CT41.

## Etapa 4 — Inscrições e pagamentos (E24–E31)

- `InscricaoService` com transação curta + `FOR UPDATE` (RI10), vagas (RN09), gratuito vs pago (RN10/RN11).
- `IGatewayPagamento` + **stub** (cobrança fictícia, simulador de webhook), `PagamentoService`, webhook idempotente (RN23).
- Cancelamento com estorno (RN14/RN15), tarefa de expiração de reservas (RN11), check-in (RN20), inscritos (E27).
- Testes: CT23–CT38, CT49–CT51. Concorrência (CT26) em teste de integração.

**Depois (fora do stub):** adaptador Asaas, resolvendo antes o ponto aberto sobre CPF/CNPJ do pagador e a validação do webhook ([decisoes.md](decisoes.md)).

## Etapa 5 — Interesse, notificações, denúncias, certificados (E32–E39)

- Interesse (E32/E33), notificações do sino (E34–E36), lembretes (RN17), reenvio de SMS (RN16).
- Denúncias com ocultação (5) e cancelamento automático após republicação (RN19), reutilizando RN14.
- Certificados: emissão em PDF, código de validação público (RN21) — decidir biblioteca de PDF.
- Testes: CT42–CT46, CT52–CT57.

## Etapa 6 — Fechamento

- Revisão da matriz de rastreabilidade e das lacunas de cobertura ([testes.md](testes.md), seção 6).
- Cobertura ≥ 70% em Services.
- Revisão de segurança (matriz de acesso, rate limiting, logs sem dados sensíveis).
- OpenAPI completo; README e guia de deploy; escolha e implementação do provedor de SMS real e do adaptador Asaas, se a equipe decidir.

## Etapa 7 — Frontend (exigido pela Entrega 3)

- Repositório/pasta `viteme-web` (React + TypeScript + Vite), conforme seção 5 do PDF: telas T01–T12, navegação, fluxos, formulários com Zod, cliente Axios com renovação automática de token, TanStack Query.
- Fluxo mínimo da demonstração: cadastro → login → mural → inscrição (gratuita e paga) → minhas inscrições → criar/publicar evento → check-in → certificado.
- Testes CT58–CT63 (Vitest + RTL + MSW).
- Decidir antes a hospedagem (mesmo site para API e frontend por causa do cookie `SameSite=Strict`).

## Etapa 8 — Fechamento da Entrega 3

- Rodar **todos** os testes e preencher [testes-execucao.md](testes-execucao.md) com resultado e evidência; [bugs.md](bugs.md) completo.
- Seed de dados e script SQL (`dotnet ef migrations script`) para demonstrar o banco.
- Documentação final em `docs/` e README completo (descrição, como executar, tecnologias, estrutura, ambiente, endpoints, banco, autenticação, guia de uso).
- Conferir [entrega-3.md](entrega-3.md) item a item.

## Convenções de trabalho por etapa

- Uma etapa = uma sequência de commits pequenos na branch `claude/wizardly-knuth-9ob62s`; sem PR até pedido explícito.
- A banca avalia a participação de cada integrante: cada um commita com nome/e-mail próprios e, preferencialmente, via branch + PR (ver [entrega-3.md](entrega-3.md), seção 6).
- Registrar testes executados e bugs durante o desenvolvimento, não no fim.
- Atualizar estes documentos quando uma decisão mudar.
- Não rodar a aplicação nem acessar o banco; validar com `dotnet build` e `dotnet test`.
