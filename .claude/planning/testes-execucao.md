# Registro de execução dos testes

Preencher **durante** o desenvolvimento. Casos e rastreabilidade em [testes.md](testes.md). Resultado: `Passou`, `Falhou`, `Corrigido` (falhou e foi corrigido), `Pendente`, `N/A`.

Evidência: saída de `dotnet test` / link da execução do CI / captura de tela. Guardar em `docs/evidencias/` (ou link do CI).

## Execuções

| Data | Commit | Comando / CI | Total | Passaram | Falharam | Observações |
|---|---|---|---|---|---|---|
| 2026-10-22 | etapa 0 | `dotnet test Viteme.sln` | 3 | 3 | 0 | Scaffold (health check e middleware de erros). Ainda não são casos CT |
| 2026-10-22 | etapa 1 | `VITEME_TESTES_DB=... dotnet test Viteme.sln` | 64 | 64 | 0 | 3 scaffold + 43 domínio + 14 persistência (PostgreSQL 16 real) + 4 Swagger. Evidência: [docs/evidencias/etapa-1-dotnet-test.txt](../../docs/evidencias/etapa-1-dotnet-test.txt). Ainda não são casos CT |

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

## Testes da etapa 1 que servem de base para casos CT

| Teste | Base para |
|---|---|
| `Quinta_falha_seguida_bloqueia_por_15_minutos`, `Depois_do_bloqueio_a_contagem_recomeca` | CT06 (RN03) |
| `Email_duplicado_sem_diferenciar_maiusculas_e_rejeitado` | CT02 (RN01, RI02) |
| `Trocar_telefone_desfaz_verificacao_e_aceite_de_sms` | CT13 (RN18) |
| `Aceite_de_sms_exige_telefone_verificado`, `Banco_rejeita_sms_ativo_sem_telefone_verificado` | CT12 (RN18, RI08) |
| `Publicar_com_data_de_inicio_passada_falha` | CT18 (RN06) |
| `Edicao_concorrente_do_evento_falha_pelo_xmin` | CT21 (RI11) |
| `Apenas_uma_inscricao_ativa_por_usuario_em_cada_evento`, `Reinscricao_e_permitida_depois_de_cancelar` | CT24 (RN08, RI05) |
| `Cancelamento_so_ate_24_horas_antes_do_inicio` | CT29, CT30 (RN15) |
| `Checkin_*` | CT49, CT50 (RN20) |
| `Envio_sms_tenta_ate_4_vezes...` | CT44 (RN16) |
| `Evento_encerrado_nao_pode_ser_cancelado`, `Encerrar_so_acontece_...` | CT22 (RN22), RN14 |
| `Denuncia_repetida_do_mesmo_usuario_e_rejeitada` | CT52 (RN19, RI06) |

Os CT só passam a constar como executados quando o endpoint correspondente existir e o caso for rodado de ponta a ponta.
