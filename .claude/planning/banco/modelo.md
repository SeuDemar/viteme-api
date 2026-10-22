# Modelo de banco de dados

PostgreSQL (Supabase), acessado por EF Core + Npgsql. Esquema versionado por migrations.

## 1. Convenções

- Tabelas e colunas em português, `snake_case`; no C#, classes em `PascalCase` (EF Core mapeia).
- Chaves primárias `uuid` geradas pela aplicação (evita sequências previsíveis nas URLs).
- Datas e horas em `timestamptz`, gravadas em **UTC**.
- Valores monetários em `numeric(10,2)`, nunca ponto flutuante.
- Status, tipos e motivos em **enums nativos** do PostgreSQL.
- `criado_em` e `atualizado_em` preenchidas automaticamente pela aplicação.
- Sem exclusão física de usuários, eventos e inscrições (eventos são cancelados; exclusão de conta futura = anonimização, preservando histórico financeiro).

## 2. Visão do relacionamento (resumo)

```
usuario 1─N evento (organizador_id)         evento 1─N atividade_programacao
usuario 1─N inscricao ─N─1 evento           evento 1─N colaborador_evento N─1 usuario
inscricao 1─N pagamento                     inscricao 1─0..1 certificado
evento 1─N alteracao_evento (autor: usuario)
usuario 1─N notificacao (evento_id opcional) ; notificacao 1─0..1 envio_sms
usuario N─N evento via interesse_evento     usuario N─N evento via denuncia
usuario 1─N codigo_verificacao ; usuario 1─N token_renovacao
```

## 3. Enums

| Tipo | Valores |
|---|---|
| `status_evento` | RASCUNHO, PUBLICADO, OCULTO, CANCELADO, ENCERRADO |
| `status_inscricao` | PENDENTE_PAGAMENTO, CONFIRMADA, CANCELADA, EXPIRADA |
| `status_pagamento` | PENDENTE, APROVADO, RECUSADO, ESTORNADO, EXPIRADO |
| `tipo_notificacao` | INSCRICAO, PAGAMENTO, ALTERACAO, LEMBRETE, CANCELAMENTO, COLABORACAO, DENUNCIA |
| `status_sms` | PENDENTE, ENVIADO, FALHOU |
| `motivo_denuncia` | EVENTO_FALSO, GOLPE, CONTEUDO_IMPROPRIO, SPAM, OUTRO |

## 4. Entidades e atributos

`PK` chave primária · `FK` chave estrangeira · `UQ` único.

### 4.1 `usuario`
Pessoa cadastrada. O papel é definido em cada evento.

| Atributo | Tipo | Restrições |
|---|---|---|
| id | uuid | PK |
| nome | varchar(120) | NOT NULL |
| email | varchar(160) | NOT NULL, UNIQUE sem diferenciar maiúsculas (`lower(email)`) |
| senha_hash | varchar(255) | NOT NULL (hash, nunca texto) |
| telefone | varchar(20) | NULL, formato E.164 |
| telefone_verificado | boolean | NOT NULL, default false |
| aceita_sms | boolean | NOT NULL, default false |
| aceite_sms_em | timestamptz | NULL (consentimento, LGPD) |
| tentativas_login | smallint | NOT NULL, default 0 |
| bloqueado_ate | timestamptz | NULL |
| criado_em / atualizado_em | timestamptz | NOT NULL |

### 4.2 `codigo_verificacao`
Código SMS para confirmar o telefone.

| Atributo | Tipo | Restrições |
|---|---|---|
| id | uuid | PK |
| usuario_id | uuid | FK → usuario, NOT NULL |
| codigo_hash | varchar(255) | NOT NULL (hash do código de 6 dígitos) |
| expira_em | timestamptz | NOT NULL (10 min) |
| tentativas | smallint | NOT NULL, default 0, CHECK ≤ 5 |
| utilizado | boolean | NOT NULL, default false |
| criado_em | timestamptz | NOT NULL |

### 4.3 `token_renovacao`
Refresh token, guardado como hash.

| Atributo | Tipo | Restrições |
|---|---|---|
| id | uuid | PK |
| usuario_id | uuid | FK → usuario, NOT NULL |
| token_hash | varchar(255) | NOT NULL, UNIQUE |
| expira_em | timestamptz | NOT NULL (7 dias) |
| revogado_em | timestamptz | NULL |
| criado_em | timestamptz | NOT NULL |

### 4.4 `evento`

| Atributo | Tipo | Restrições |
|---|---|---|
| id | uuid | PK |
| organizador_id | uuid | FK → usuario, NOT NULL |
| titulo | varchar(150) | NOT NULL |
| descricao | text | NOT NULL |
| categoria | varchar(60) | NOT NULL |
| local | varchar(200) | NOT NULL (local ou link) |
| imagem_url | varchar(500) | NULL |
| data_inicio | timestamptz | NOT NULL |
| data_fim | timestamptz | NOT NULL, CHECK > data_inicio |
| inscricao_inicio | timestamptz | NOT NULL |
| inscricao_fim | timestamptz | NOT NULL, CHECK > inscricao_inicio e ≤ data_inicio |
| capacidade | integer | NULL (nulo = ilimitado), CHECK > 0 |
| preco | numeric(10,2) | NOT NULL, default 0, CHECK ≥ 0 |
| carga_horaria | smallint | NULL, CHECK > 0 |
| status | status_evento | NOT NULL, default RASCUNHO |
| reaberto_em | timestamptz | NULL (republicação após ocultação, RN19) |
| criado_em / atualizado_em | timestamptz | NOT NULL |
| xmin | (sistema) | token de concorrência otimista → ETag / `If-Match` |

### 4.5 `atividade_programacao`

| Atributo | Tipo | Restrições |
|---|---|---|
| id | uuid | PK |
| evento_id | uuid | FK → evento, NOT NULL |
| titulo | varchar(150) | NOT NULL |
| palestrante | varchar(120) | NULL |
| local | varchar(200) | NULL |
| inicio | timestamptz | NOT NULL |
| fim | timestamptz | NOT NULL, CHECK > inicio |

### 4.6 `colaborador_evento`

| Atributo | Tipo | Restrições |
|---|---|---|
| id | uuid | PK |
| evento_id | uuid | FK → evento, NOT NULL |
| usuario_id | uuid | FK → usuario, NOT NULL |
| adicionado_em | timestamptz | NOT NULL |
| — | — | UNIQUE (evento_id, usuario_id) |

### 4.7 `inscricao`

| Atributo | Tipo | Restrições |
|---|---|---|
| id | uuid | PK |
| evento_id | uuid | FK → evento, NOT NULL |
| usuario_id | uuid | FK → usuario, NOT NULL |
| status | status_inscricao | NOT NULL |
| codigo_checkin | varchar(12) | NOT NULL, UNIQUE |
| reservada_ate | timestamptz | NULL (eventos pagos) |
| presenca_confirmada | boolean | NOT NULL, default false |
| checkin_em | timestamptz | NULL |
| criado_em / atualizado_em | timestamptz | NOT NULL |

### 4.8 `pagamento`

| Atributo | Tipo | Restrições |
|---|---|---|
| id | uuid | PK |
| inscricao_id | uuid | FK → inscricao, NOT NULL |
| valor | numeric(10,2) | NOT NULL, CHECK > 0 (igual ao preço) |
| status | status_pagamento | NOT NULL |
| metodo | varchar(30) | NULL (Pix, cartão etc., informado pelo gateway) |
| id_transacao_gateway | varchar(100) | NULL, UNIQUE (preenchido após criar a cobrança) |
| url_pagamento | varchar(500) | NULL |
| criado_em / atualizado_em | timestamptz | NOT NULL |

### 4.9 `alteracao_evento`
Histórico de alterações de evento publicado (base do aviso de alteração).

| Atributo | Tipo | Restrições |
|---|---|---|
| id | uuid | PK |
| evento_id | uuid | FK → evento, NOT NULL |
| autor_id | uuid | FK → usuario, NOT NULL |
| campo | varchar(50) | NOT NULL |
| valor_anterior / valor_novo | text | NULL |
| motivo | varchar(300) | NOT NULL |
| criado_em | timestamptz | NOT NULL |

### 4.10 `notificacao`

| Atributo | Tipo | Restrições |
|---|---|---|
| id | uuid | PK |
| usuario_id | uuid | FK → usuario, NOT NULL |
| evento_id | uuid | FK → evento, NULL |
| tipo | tipo_notificacao | NOT NULL |
| titulo | varchar(150) | NOT NULL |
| mensagem | varchar(1000) | NOT NULL |
| lida | boolean | NOT NULL, default false |
| criado_em | timestamptz | NOT NULL |

### 4.11 `envio_sms`

| Atributo | Tipo | Restrições |
|---|---|---|
| id | uuid | PK |
| notificacao_id | uuid | FK → notificacao, NOT NULL, UNIQUE |
| telefone | varchar(20) | NOT NULL |
| status | status_sms | NOT NULL |
| tentativas | smallint | NOT NULL, default 0, CHECK ≤ 4 |
| ultimo_erro | varchar(300) | NULL |
| enviado_em | timestamptz | NULL |
| criado_em | timestamptz | NOT NULL |

### 4.12 `interesse_evento`
PK composta (`usuario_id`, `evento_id`), ambos FK; `criado_em`.

### 4.13 `denuncia`

| Atributo | Tipo | Restrições |
|---|---|---|
| id | uuid | PK |
| evento_id | uuid | FK → evento, NOT NULL |
| usuario_id | uuid | FK → usuario, NOT NULL |
| motivo | motivo_denuncia | NOT NULL |
| descricao | varchar(500) | NULL (obrigatória se motivo = OUTRO) |
| criado_em | timestamptz | NOT NULL |
| — | — | UNIQUE (evento_id, usuario_id) |

### 4.14 `certificado`

| Atributo | Tipo | Restrições |
|---|---|---|
| id | uuid | PK |
| inscricao_id | uuid | FK → inscricao, NOT NULL, UNIQUE |
| codigo_validacao | varchar(20) | NOT NULL, UNIQUE |
| emitido_em | timestamptz | NOT NULL |

## 5. Políticas de exclusão (`ON DELETE`)

Maioria `RESTRICT`; `CASCADE` só em registros que existem em função do pai.

| Relacionamento | ON DELETE |
|---|---|
| usuario → codigo_verificacao, token_renovacao, colaborador_evento, interesse_evento | CASCADE |
| evento → atividade_programacao, colaborador_evento, interesse_evento | CASCADE |
| notificacao → envio_sms | CASCADE |
| evento → notificacao (evento_id opcional) | SET NULL |
| usuario → evento (organizador), inscricao, notificacao, alteracao_evento (autor), denuncia | RESTRICT |
| evento → inscricao, alteracao_evento, denuncia | RESTRICT |
| inscricao → pagamento, certificado | RESTRICT |

## 6. Regras de integridade

| Cód. | Regra |
|---|---|
| RI01 | Toda tabela tem PK; `interesse_evento` usa PK composta |
| RI02 | Índice único em `lower(usuario.email)` (RN01) |
| RI03 | CHECK em `evento`: `data_fim > data_inicio`, `inscricao_fim > inscricao_inicio`, `inscricao_fim <= data_inicio` (RN05) |
| RI04 | CHECK de valores: `preco >= 0`, `capacidade > 0`, `carga_horaria > 0`, `pagamento.valor > 0`, `atividade_programacao.fim > inicio` |
| RI05 | Índice único **parcial** em `inscricao (evento_id, usuario_id) WHERE status IN ('PENDENTE_PAGAMENTO','CONFIRMADA')`: no máximo uma inscrição ativa por usuário/evento (RN08); permite reinscrever após cancelar/expirar |
| RI06 | UNIQUE em `colaborador_evento (evento_id, usuario_id)` e `denuncia (evento_id, usuario_id)` (RN04, RN19) |
| RI07 | UNIQUE em `pagamento.id_transacao_gateway`, `inscricao.codigo_checkin`, `certificado.inscricao_id`, `certificado.codigo_validacao`, `envio_sms.notificacao_id`, `token_renovacao.token_hash`. UNIQUE aceita vários nulos, o que permite gravar o pagamento antes de o gateway devolver o id |
| RI08 | CHECK em `usuario`: `NOT aceita_sms OR telefone_verificado` (RN18) e telefone em E.164 (`^\+[1-9][0-9]{7,14}$`) |
| RI09 | Todas as FKs são `NOT NULL`, exceto `notificacao.evento_id` (`SET NULL`) |
| RI10 | Controle de vagas sob concorrência: a inscrição carrega o evento com `SELECT ... FOR UPDATE` dentro da transação (RN09) |
| RI11 | Concorrência otimista na edição de eventos: `xmin` como token de versão do EF Core, exposto como ETag (`If-Match`) |
| RI12 | Regras que dependem de outras tabelas (atividade dentro do período, preço bloqueado com inscrições) são validadas nos services, na mesma transação da gravação |

## 7. Índices previstos (além de PK/UNIQUE)

- `evento (status, data_inicio)`: consulta principal do mural.
- `evento (organizador_id)` e `colaborador_evento (usuario_id)`: tela "Meus eventos".
- `inscricao (evento_id, status)` e `inscricao (usuario_id)`: contagem de vagas e "Minhas inscrições".
- `inscricao (reservada_ate) WHERE status = 'PENDENTE_PAGAMENTO'`: tarefa de expiração de reservas.
- `notificacao (usuario_id, lida, criado_em DESC)`: sino de notificações.
- `alteracao_evento (evento_id, criado_em DESC)` e `denuncia (evento_id, criado_em)`: histórico e contagem de denúncias.
