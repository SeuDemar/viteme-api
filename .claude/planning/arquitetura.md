# Arquitetura

## 1. Visão geral

Cliente-servidor. Frontend SPA (futuro) consome uma **API REST em ASP.NET Core** sobre HTTPS + JSON (Bearer). O backend é um **monólito modular** em camadas inspirado em Clean Architecture: regras de negócio no centro, sem depender de banco, framework web ou serviços externos; essas dependências ficam atrás de interfaces implementadas na infraestrutura.

```
Cliente (HTTPS + JSON, Bearer)
  └─ Viteme.Api            controllers finos, middlewares, auth, CORS, rate limiting, OpenAPI
       └─ Viteme.Application   services (regras), DTOs, validadores, interfaces (portas)
            └─ Viteme.Domain       entidades e enums, invariantes simples
       └─ Viteme.Infrastructure    EF Core/Npgsql, repositories, adaptadores (gateway, SMS), tarefas
                └─ PostgreSQL (Supabase) · Gateway de pagamento (Asaas) · Provedor de SMS
```

Regra de dependência: camadas externas dependem das internas, nunca o contrário.

## 2. Projetos da solution (`Viteme.sln` na raiz)

| Projeto | Responsabilidade | Depende de |
|---|---|---|
| `Viteme.Domain` | Entidades, enums e regras que pertencem à própria entidade (ex.: um evento sabe se está aberto para inscrição) | — |
| `Viteme.Application` | Services com as regras de negócio, DTOs, validadores e interfaces (repositórios, gateway, notificações) | Domain |
| `Viteme.Infrastructure` | DbContext, mapeamentos, migrations, adaptadores do gateway e do SMS, tarefas em segundo plano | Application, Domain |
| `Viteme.Api` | Controllers, middlewares, auth, CORS, rate limiting, DI, OpenAPI | Application, Infrastructure |
| `Viteme.Tests` | Testes unitários e de integração | Todos |

## 3. Estrutura de pastas prevista

| Pasta | Conteúdo |
|---|---|
| `Viteme.Api/Controllers` | `AuthController`, `UsuariosController`, `EventosController`, `AtividadesController`, `ColaboradoresController`, `InscricoesController`, `PagamentosController`, `NotificacoesController`, `DenunciasController`, `CertificadosController` |
| `Viteme.Api/Middlewares` | Tratamento global de exceções (→ ProblemDetails) e registro de requisições |
| `Viteme.Api/Autorizacao` | Políticas e handlers de autorização por papel no evento |
| `Viteme.Application/Services` | `AuthService`, `UsuarioService`, `EventoService`, `InscricaoService`, `PagamentoService`, `NotificacaoService`, `DenunciaService`, `CertificadoService` |
| `Viteme.Application/DTOs` | Entrada e saída de cada endpoint (ex.: `EventoCriacaoDto`, `EventoDetalheDto`, `InscricaoDto`) |
| `Viteme.Application/Validacoes` | Validadores dos DTOs de entrada |
| `Viteme.Application/Interfaces` | `IEventoRepository`, `IInscricaoRepository`, `IUnitOfWork`, `IGatewayPagamento`, `INotificationSender`, `ITokenService`, `IRelogio` |
| `Viteme.Domain/Entities` | `Usuario`, `Evento`, `AtividadeProgramacao`, `ColaboradorEvento`, `Inscricao`, `Pagamento`, `AlteracaoEvento`, `Notificacao`, `EnvioSms`, `InteresseEvento`, `Denuncia`, `Certificado`, `CodigoVerificacao`, `TokenRenovacao` |
| `Viteme.Domain/Enums` | `StatusEvento`, `StatusInscricao`, `StatusPagamento`, `TipoNotificacao`, `StatusSms`, `MotivoDenuncia` |
| `Viteme.Infrastructure/Persistencia` | `VitemeDbContext`, configurações Fluent API, migrations, repositories |
| `Viteme.Infrastructure/Externos` | Adaptador do gateway (stub → Asaas) e do provedor de SMS (stub → real) |
| `Viteme.Infrastructure/Tarefas` | `BackgroundService`s: expiração de reservas, encerramento de eventos, lembretes, reenvio de SMS |

## 4. Responsabilidade de cada componente

| Componente | Responsabilidade |
|---|---|
| API | Entrada HTTP sob `/api/v1`. JSON, JWT, CORS, rate limiting e erros padronizados. OpenAPI (Swagger) em desenvolvimento |
| Controllers | Finos: validam formato, chamam o service e convertem o resultado em HTTP. Sem regra de negócio e sem acesso a banco |
| Services | Concentram RN01–RN23: permissões por papel, vagas, ciclo de vida de evento/inscrição, pagamentos, alterações e notificações. Coordenam repositories e serviços externos e definem os limites das transações |
| Repositories | Encapsulam o EF Core. Consultas com significado (`ContarVagasOcupadasAsync`, `ObterParaAtualizacaoAsync` com bloqueio). `IUnitOfWork` confirma em uma transação |
| Entities | Estado e comportamento (`Evento.Publicar()`, `Inscricao.Confirmar()`); protegem invariantes simples (evento cancelado não publica) |
| DTOs | Contrato da API; nunca expõem `senha_hash`; cada endpoint tem DTOs próprios de entrada e saída |
| Banco | PostgreSQL via Npgsql; esquema versionado por migrations; restrições de integridade também no banco |
| Auth | JWT de acesso curto + refresh rotativo; políticas do ASP.NET Core verificam o papel **no evento** |

## 5. Fluxo de uma requisição — inscrição em evento pago (E24)

1. `POST /api/v1/eventos/{id}/inscricoes` com o token de acesso.
2. Middleware valida o JWT e identifica o usuário; rate limiting verifica o limite.
3. `InscricoesController` chama `InscricaoService.InscreverAsync(eventoId, usuarioId)`.
4. O service abre uma **transação curta**, carrega o evento com **bloqueio de linha** (`FOR UPDATE`) e verifica RN08/RN09 (status, período, vagas, papel do usuário).
5. Na mesma transação grava a inscrição `PENDENTE_PAGAMENTO` com reserva de 30 min e o pagamento `PENDENTE`; confirma a transação (libera o bloqueio).
6. **Fora da transação**, pede a cobrança a `IGatewayPagamento` pelo valor exato do evento (RN12), usando o **id do pagamento como referência externa e chave de idempotência**. Assim nunca existe cobrança sem registro no banco.
7. Gateway respondeu → service grava `id_transacao_gateway` e `url_pagamento`; controller devolve **201**.
   Gateway falhou → pagamento `EXPIRADO`, inscrição `EXPIRADA`, vaga liberada, API devolve **502**.
8. Após o pagamento, o gateway chama o webhook (E31). `PagamentoService` valida a assinatura, localiza o pagamento pela referência externa, confirma a inscrição e `NotificacaoService` avisa o usuário no site e, se houver aceite, por SMS.

Evento **gratuito**: a inscrição já nasce `CONFIRMADA` (RN10), sem pagamento.

## 6. Integrações externas

Ficam atrás de interfaces da camada de aplicação, implementadas em `Viteme.Infrastructure/Externos`.

- **`IGatewayPagamento`**: `CriarCobrancaAsync`, `ConsultarCobrancaAsync`, `SolicitarEstornoAsync`, `ValidarAssinaturaWebhook`.
  - Agora: **stub** (devolve id e URL fictícios, permite simular webhook em testes e desenvolvimento).
  - Depois: adaptador **Asaas** (ver pontos em aberto em [decisoes.md](decisoes.md)).
- **`INotificationSender`**: contrato único de envio, duas implementações:
  - `NotificacaoSiteSender`: grava a notificação do sino do site.
  - `NotificacaoSmsSender`: registra o envio (`envio_sms`) e chama o provedor de SMS. **Agora: stub** que só registra/loga.
  - `NotificacaoService` decide os canais por usuário conforme RN16.
  - Novo canal (e-mail, WhatsApp) = nova implementação, sem mexer no que existe.
- **`IRelogio`**: abstrai o tempo para testar reserva de 30 min, cancelamento em 24 h, validade de código etc.
- **`ITokenService`**: emissão/validação de JWT e geração/hash de refresh token.

## 7. Tarefas em segundo plano (`BackgroundService`)

| Frequência | Tarefa | Regra |
|---|---|---|
| 1 min | Expirar reservas vencidas (inscrição `PENDENTE_PAGAMENTO` com `reservada_ate` passada → `EXPIRADA`, pagamento `EXPIRADO`) | RN11 |
| 1 min | Reenviar SMS que falharam (até 3 novas tentativas) | RN16 |
| 15 min | Encerrar eventos `PUBLICADO` com `data_fim` vencida | RN22 |
| 15 min | Enviar lembretes (24 h antes do início → inscritos confirmados; 24 h antes do fim das inscrições → interessados não inscritos) | RN17 |

As tarefas precisam ser **idempotentes** e seguras com mais de uma instância (usar condições no `UPDATE`/bloqueios curtos).

## 8. Justificativas (Quadro 4 da spec, resumido)

- Camadas: separa responsabilidades e permite testar services com mocks.
- Interfaces para repositórios/externos: regras não conhecem banco nem fornecedores; gateway escolhido depois.
- Monólito modular: menos custo operacional que microsserviços, mantendo a divisão em projetos.
- PostgreSQL: dados relacionais; vagas e pagamentos exigem transações e restrições.
- JWT + refresh rotativo: API sem estado de sessão; token curto limita vazamento.
- Papel por evento sem administrador: decisão da equipe; risco tratado por denúncias (RN19).
