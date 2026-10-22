# Registro de execução dos testes

Preencher **durante** o desenvolvimento. Casos e rastreabilidade em [testes.md](testes.md). Resultado: `Passou`, `Falhou`, `Corrigido` (falhou e foi corrigido), `Pendente`, `N/A`.

Evidência: saída de `dotnet test` / link da execução do CI / captura de tela. Guardar em `docs/evidencias/` (ou link do CI).

## Execuções

| Data | Commit | Comando / CI | Total | Passaram | Falharam | Observações |
|---|---|---|---|---|---|---|
| — | db48742 | `dotnet test Viteme.sln` | 3 | 3 | 0 | Scaffold (health check e middleware de erros). Ainda não são casos CT |

## Casos de teste

| ID | Cenário | Nível | Resultado | Data | Evidência | Bug |
|---|---|---|---|---|---|---|
| CT01–CT07 | Autenticação e conta | Unit/Integração | Pendente | | | |
| CT08–CT13 | Telefone e SMS | Unit/Integração | Pendente | | | |
| CT14–CT22 | Eventos | Unit/Integração | Pendente | | | |
| CT23–CT30 | Inscrições | Unit/Integração | Pendente | | | |
| CT31–CT38 | Pagamentos | Unit/Integração | Pendente | | | |
| CT39–CT46 | Alterações, cancelamento, notificações | Unit/Integração | Pendente | | | |
| CT47–CT51 | Colaboradores e check-in | Unit/Integração | Pendente | | | |
| CT52–CT57 | Denúncias e certificados | Unit/Integração | Pendente | | | |
| CT58–CT63 | Frontend | Componente | Pendente | | | |

> Quando um caso for executado, abrir uma linha por CT (CT01, CT02…) em vez da faixa.
