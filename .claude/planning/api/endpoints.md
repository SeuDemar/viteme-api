# Endpoints (E01–E39)

Prefixo `/api/v1`. Convenções e erros: [convencoes-e-erros.md](convencoes-e-erros.md). Regras: [regras-de-negocio.md](regras-de-negocio.md).

Legenda de acesso: **Público** · **Autenticado** · **Organizador** · **Org./Colab.** (organizador ou colaborador do evento da rota) · **Dono** (dono do recurso; sempre filtrado pelo `sub` do token) · **Gateway** (assinatura do gateway, sem JWT).

## 1. Resumo

| Cód. | Método | Rota | Acesso | Objetivo |
|---|---|---|---|---|
| E01 | POST | `/auth/cadastro` | Público | Criar conta |
| E02 | POST | `/auth/login` | Público | Autenticar e emitir tokens |
| E03 | POST | `/auth/renovar` | Público | Novo token de acesso a partir do refresh |
| E04 | POST | `/auth/logout` | Público | Encerrar a sessão |
| E05 | GET | `/usuarios/me` | Autenticado | Consultar o próprio perfil |
| E06 | PUT | `/usuarios/me` | Autenticado | Atualizar nome e telefone |
| E07 | POST | `/usuarios/me/telefone/codigo` | Autenticado | Enviar código SMS de verificação |
| E08 | POST | `/usuarios/me/telefone/verificar` | Autenticado | Confirmar telefone com o código |
| E09 | PUT | `/usuarios/me/preferencias-sms` | Autenticado | Ativar/revogar aceite de SMS |
| E10 | GET | `/eventos` | Público | Listar eventos do mural |
| E11 | GET | `/eventos/{id}` | Público | Detalhes de um evento |
| E12 | POST | `/eventos` | Autenticado | Criar evento (criador = organizador) |
| E13 | PUT | `/eventos/{id}` | Organizador | Editar evento |
| E14 | POST | `/eventos/{id}/publicar` | Organizador | Publicar (ou republicar após ocultação) |
| E15 | POST | `/eventos/{id}/cancelar` | Organizador | Cancelar evento |
| E16 | GET | `/eventos/meus` | Autenticado | Eventos que organiza ou colabora |
| E17 | GET | `/eventos/{id}/alteracoes` | Público | Histórico de alterações |
| E18 | POST | `/eventos/{id}/atividades` | Organizador | Adicionar atividade |
| E19 | PUT | `/eventos/{id}/atividades/{atividadeId}` | Organizador | Editar atividade |
| E20 | DELETE | `/eventos/{id}/atividades/{atividadeId}` | Organizador | Remover atividade |
| E21 | GET | `/eventos/{id}/colaboradores` | Org./Colab. | Listar colaboradores |
| E22 | POST | `/eventos/{id}/colaboradores` | Organizador | Adicionar colaborador por e-mail |
| E23 | DELETE | `/eventos/{id}/colaboradores/{usuarioId}` | Organizador | Remover colaborador |
| E24 | POST | `/eventos/{id}/inscricoes` | Autenticado | Inscrever-se |
| E25 | GET | `/inscricoes/minhas` | Autenticado | Listar inscrições do usuário |
| E26 | POST | `/inscricoes/{id}/cancelar` | Dono | Cancelar a própria inscrição |
| E27 | GET | `/eventos/{id}/inscricoes` | Org./Colab. | Listar inscritos |
| E28 | POST | `/eventos/{id}/checkin` | Org./Colab. | Registrar presença |
| E29 | POST | `/inscricoes/{id}/pagamento` | Dono | Gerar ou retomar cobrança |
| E30 | GET | `/inscricoes/{id}/pagamento` | Dono | Situação do pagamento |
| E31 | POST | `/pagamentos/webhook` | Gateway | Receber mudança de status |
| E32 | POST | `/eventos/{id}/interesse` | Autenticado | Acompanhar evento |
| E33 | DELETE | `/eventos/{id}/interesse` | Autenticado | Deixar de acompanhar |
| E34 | GET | `/notificacoes` | Autenticado | Listar notificações |
| E35 | PATCH | `/notificacoes/{id}/lida` | Dono | Marcar como lida |
| E36 | PATCH | `/notificacoes/lidas` | Autenticado | Marcar todas como lidas |
| E37 | POST | `/eventos/{id}/denuncias` | Autenticado | Denunciar evento |
| E38 | GET | `/inscricoes/{id}/certificado` | Dono | Emitir (1ª vez) e baixar PDF |
| E39 | GET | `/certificados/{codigo}/validacao` | Público | Validar autenticidade |

## 2. Especificação

Formato: **Entrada → Saída**; RN aplicadas; validações; erros possíveis. Erros 401 estão implícitos em todo endpoint autenticado.

### 2.1 Autenticação e conta

**E01 — POST /auth/cadastro** (Público) · RN01, RN02
- Entrada: `{ nome, email, senha, telefone? }`
- Saída: `201 { id, nome, email }`
- Validações: nome 3–120; e-mail válido ≤ 160; senha conforme RN02; telefone opcional E.164 (ex.: `+5544999990000`).
- Erros: 400; 409 `EMAIL_JA_CADASTRADO`; 429.

**E02 — POST /auth/login** (Público) · RN03
- Entrada: `{ email, senha }`
- Saída: `200 { tokenAcesso, expiraEm, usuario: { id, nome, email, telefoneVerificado, aceitaSms } }` + cookie HttpOnly com o refresh.
- Validações: e-mail e senha obrigatórios; **mesma mensagem** para e-mail inexistente e senha incorreta.
- Erros: 400; 401 `CREDENCIAIS_INVALIDAS`; 423 `CONTA_BLOQUEADA`; 429.

**E03 — POST /auth/renovar** (Público)
- Entrada: cookie `refresh_token`.
- Saída: `200 { tokenAcesso, expiraEm }` + novo cookie (rotação).
- Validações: token existe, não expirou, não revogado. **Reuso de token revogado revoga todos os tokens do usuário.**
- Erros: 401 `TOKEN_INVALIDO`.

**E04 — POST /auth/logout** (Público)
- Entrada: cookie `refresh_token`. Saída: `204`; cookie removido.
- Não exige token de acesso (sair mesmo com token expirado); revoga o refresh do cookie, se houver; **idempotente**; nunca retorna erro (sempre 204).

**E05 — GET /usuarios/me** (Autenticado)
- Saída: `200 { id, nome, email, telefone, telefoneVerificado, aceitaSms, aceiteSmsEm }`. Erros: 401.

**E06 — PUT /usuarios/me** (Autenticado) · RN18
- Entrada: `{ nome, telefone? }`. Saída: `200` perfil atualizado.
- Mesmas validações do cadastro. **Trocar o telefone zera a verificação e o aceite de SMS.** Erros: 400; 401.

**E07 — POST /usuarios/me/telefone/codigo** (Autenticado) · RN18
- Saída: `202 { expiraEm }`. Validação: telefone cadastrado e ainda não verificado.
- Erros: 401; 422 `TELEFONE_NAO_INFORMADO`, `TELEFONE_JA_VERIFICADO`; 429 `AGUARDE_NOVO_CODIGO`.

**E08 — POST /usuarios/me/telefone/verificar** (Autenticado) · RN18
- Entrada: `{ codigo }` (exatamente 6 dígitos). Saída: `200 { telefoneVerificado: true }`.
- Erros: 400; 401; 422 `CODIGO_INVALIDO`, `CODIGO_EXPIRADO`, `TENTATIVAS_EXCEDIDAS`.

**E09 — PUT /usuarios/me/preferencias-sms** (Autenticado) · RN18
- Entrada: `{ aceitaSms }` (boolean obrigatório). Saída: `200 { aceitaSms, aceiteSmsEm }`.
- Erros: 400; 401; 422 `TELEFONE_NAO_VERIFICADO`.

### 2.2 Eventos e programação

**E10 — GET /eventos** (Público) · RN06, RN09
- Query: `busca`, `categoria`, `dataDe`, `dataAte`, `gratuito`, `pagina` (padrão 1), `tamanhoPagina` (padrão 20, máx. 50).
- Saída: `200 { itens: [{ id, titulo, categoria, local, dataInicio, preco, vagasRestantes, imagemUrl, alteradoRecentemente }], pagina, tamanhoPagina, total }`.
- `dataDe ≤ dataAte`; só eventos PUBLICADO ainda não encerrados; `alteradoRecentemente` = houve alteração nas últimas **72 h**. Erros: 400.

**E11 — GET /eventos/{id}** (Público) · RN06, RN09, RN19
- Saída: `200` dados completos + `atividades`, `ultimasAlteracoes`, `vagasRestantes`, `organizador { id, nome }` e, se autenticado, `minhaInscricao`, `souOrganizador`, `souColaborador`, `interessado`.
- Rascunho só visível a organizador e colaboradores; OCULTO também visível aos inscritos.
- Erros: 404 `EVENTO_NAO_ENCONTRADO` (inclusive para quem não pode ver).

**E12 — POST /eventos** (Autenticado) · RN05, RN06, RN12
- Entrada: `{ titulo, descricao, categoria, local, imagemUrl?, dataInicio, dataFim, inscricaoInicio, inscricaoFim, capacidade?, preco, cargaHoraria? }`
- Saída: `201` + `Location`; evento `RASCUNHO`.
- Validações: título 5–150; descrição ≤ 5000; preço 0–99.999,99 (2 casas); capacidade e carga horária > 0 quando informadas; `imagemUrl` https; datas conforme RN05. Erros: 400; 401.

**E13 — PUT /eventos/{id}** (Organizador) · RN05, RN07, RN13
- Cabeçalho `If-Match` com a versão (ETag recebido em E11/E12; sem o cabeçalho a API responde 428 `VERSAO_OBRIGATORIA`). Entrada: campos de E12 + `motivoAlteracao` (obrigatório quando RN13 se aplica; 10–300).
- Saída: `200` evento atualizado.
- Erros: 400; 401; 403; 404; 409 `EVENTO_FINALIZADO` (cancelado/encerrado); 412 `VERSAO_DESATUALIZADA`; 422 `PRECO_BLOQUEADO`, `CAPACIDADE_ABAIXO_DAS_VAGAS_OCUPADAS`, `MOTIVO_OBRIGATORIO`.

**E14 — POST /eventos/{id}/publicar** (Organizador) · RN06, RN19
- Saída: `200 { status: "PUBLICADO" }`. Status atual RASCUNHO ou OCULTO (republicação registra `reaberto_em`; segunda ocultação cancela — RN19).
- Erros: 401; 403; 404; 409 `EVENTO_FINALIZADO`; 422 `DADOS_INCOMPLETOS`, `DATA_INICIO_PASSADA`.

**E15 — POST /eventos/{id}/cancelar** (Organizador) · RN14
- Entrada: `{ motivo }` (10–300). Saída: `200 { status: "CANCELADO" }`.
- Erros: 400; 401; 403; 404; 409 `EVENTO_JA_CANCELADO`; 422 `EVENTO_ENCERRADO`.

**E16 — GET /eventos/meus** (Autenticado) · RN04
- Query: `papel` (organizador | colaborador), `status`. Saída: `200` lista de eventos resumidos com o campo `papel`. Erros: 400; 401.

**E17 — GET /eventos/{id}/alteracoes** (Público) · RN13
- Saída: `200 [{ campo, valorAnterior, valorNovo, motivo, criadoEm }]`, da mais recente para a mais antiga. Mesma visibilidade de E11. Erros: 404.

**E18 — POST /eventos/{id}/atividades** (Organizador) · RN05, RN07, RN13
- Entrada: `{ titulo, palestrante?, local?, inicio, fim, motivoAlteracao? }` (o motivo é obrigatório em evento publicado; acréscimo à spec, ver D20). Saída: `201` atividade criada.
- Validações: título 3–150; `fim > inicio`; período dentro do evento. Erros: 400; 401; 403; 404; 422 `ATIVIDADE_FORA_DO_PERIODO`.

**E19 — PUT /eventos/{id}/atividades/{atividadeId}** (Organizador) · RN05, RN07, RN13
- Entrada: como E18 (+ `motivoAlteracao` se o evento estiver publicado). Saída: `200`.
- Erros: 400; 401; 403; 404; 422 `ATIVIDADE_FORA_DO_PERIODO`, `MOTIVO_OBRIGATORIO`.

**E20 — DELETE /eventos/{id}/atividades/{atividadeId}** (Organizador) · RN07, RN13
- Query `motivo` (obrigatório se publicado). Saída: `204`. Erros: 401; 403; 404; 422 `MOTIVO_OBRIGATORIO`.

### 2.3 Colaboradores

**E21 — GET /eventos/{id}/colaboradores** (Org./Colab.) · RN07
- Saída: `200 [{ usuarioId, nome, email, adicionadoEm }]`. Erros: 401; 403; 404.

**E22 — POST /eventos/{id}/colaboradores** (Organizador) · RN04, RN07, RN08
- Entrada: `{ email }`. Saída: `201` colaborador adicionado; o usuário recebe notificação (tipo `COLABORACAO`).
- Validações: e-mail de usuário cadastrado, **sem inscrição ativa** no evento.
- Erros: 400; 401; 403; 404 `USUARIO_NAO_ENCONTRADO`; 409 `JA_COLABORADOR`; 422 `ORGANIZADOR_NAO_PODE_SER_COLABORADOR`, `USUARIO_JA_INSCRITO`.

**E23 — DELETE /eventos/{id}/colaboradores/{usuarioId}** (Organizador) · RN07
- Saída: `204`. Erros: 401; 403; 404.

### 2.4 Inscrições e check-in

**E24 — POST /eventos/{id}/inscricoes** (Autenticado) · RN08–RN12
- Saída: `201 { id, status, codigoCheckin?, reservadaAte?, pagamento?: { id, valor, urlPagamento } }`. Gratuito → CONFIRMADA; pago → PENDENTE_PAGAMENTO + URL de pagamento.
- Contagem de vagas + gravação da inscrição e do pagamento em **uma transação curta** com bloqueio da linha do evento; chamada ao gateway **depois** da confirmação, com o id do pagamento como referência externa e chave de idempotência.
- Erros: 401; 404; 409 `INSCRICAO_EXISTENTE`; 422 `EVENTO_INDISPONIVEL`, `INSCRICOES_ENCERRADAS`, `SEM_VAGAS`, `PAPEL_NAO_PERMITE_INSCRICAO`; 502 `GATEWAY_INDISPONIVEL` (inscrição pendente passa a EXPIRADA e a vaga é liberada).

**E25 — GET /inscricoes/minhas** (Autenticado) · RN21
- Query `situacao` (proximas | passadas | todas).
- Saída: `200 [{ id, evento: { id, titulo, dataInicio, local }, status, codigoCheckin, reservadaAte, presencaConfirmada, certificadoDisponivel }]`. Erros: 400; 401.

**E26 — POST /inscricoes/{id}/cancelar** (Dono) · RN15
- Saída: `200 { status: "CANCELADA", estornoSolicitado }`.
- Erros: 401; 403; 404; 409 `INSCRICAO_JA_CANCELADA`; 422 `PRAZO_CANCELAMENTO_ENCERRADO`.

**E27 — GET /eventos/{id}/inscricoes** (Org./Colab.) · RN07
- Query: `status`, `busca`, `pagina`, `tamanhoPagina`.
- Saída: `200` página com `{ inscricaoId, nome, email, status, presencaConfirmada, checkinEm }`. **O telefone não é exibido** (minimização de dados). Erros: 400; 401; 403; 404.

**E28 — POST /eventos/{id}/checkin** (Org./Colab.) · RN07, RN20
- Entrada: `{ codigoCheckin }` (12 alfanuméricos; convertido para maiúsculas). Saída: `200 { nome, checkinEm }`.
- Erros: 400; 401; 403; 404 `INSCRICAO_NAO_ENCONTRADA`; 409 `CHECKIN_JA_REALIZADO`; 422 `INSCRICAO_NAO_CONFIRMADA`, `FORA_DO_HORARIO_DE_CHECKIN`.

### 2.5 Pagamentos

**E29 — POST /inscricoes/{id}/pagamento** (Dono) · RN11, RN12
- Saída: `200` (cobrança pendente existente) ou `201` (nova): `{ pagamentoId, status, valor, urlPagamento }`.
- Nova cobrança só se a anterior estiver RECUSADO ou EXPIRADO e a reserva continuar válida.
- Erros: 401; 403; 404; 409 `PAGAMENTO_JA_APROVADO`; 422 `RESERVA_EXPIRADA`, `EVENTO_GRATUITO`; 502 `GATEWAY_INDISPONIVEL`.

**E30 — GET /inscricoes/{id}/pagamento** (Dono) · RN11
- Saída: `200 { status, valor, metodo, atualizadoEm, statusInscricao }` (usado pelo frontend ao voltar do gateway). Erros: 401; 403; 404.

**E31 — POST /pagamentos/webhook** (Gateway) · RN11, RN14, RN15, RN23
- Cabeçalho com a assinatura do gateway. Entrada: `{ idTransacao, referenciaExterna, status, valor, ... }`.
- Saída: `200` (processado ou já processado antes).
- Validações: assinatura válida (ver [../decisoes.md](../decisoes.md), ponto em aberto sobre HMAC vs token do Asaas); pagamento localizado pela **referência externa** (id do pagamento); valor igual ao registrado. **Não usa JWT nem passa pelo rate limiting.**
- Erros: 401 `ASSINATURA_INVALIDA`; 404 `TRANSACAO_DESCONHECIDA`; 422 `VALOR_DIVERGENTE`.

### 2.6 Interesse e notificações

**E32 — POST /eventos/{id}/interesse** (Autenticado) · RN13, RN17
- Saída: `204` (idempotente). Validação: evento PUBLICADO. Erros: 401; 404; 422 `EVENTO_INDISPONIVEL`.

**E33 — DELETE /eventos/{id}/interesse** (Autenticado)
- Saída: `204` (idempotente). Erros: 401.

**E34 — GET /notificacoes** (Autenticado) · RN16
- Query: `lidas` (true|false), `pagina`, `tamanhoPagina`.
- Saída: `200 { itens: [{ id, tipo, titulo, mensagem, eventoId, lida, criadoEm }], naoLidas, pagina, total }`. Erros: 400; 401.

**E35 — PATCH /notificacoes/{id}/lida** (Dono)
- Saída: `204`. A notificação deve ser do usuário. Erros: 401; 404 (inclusive para notificação de outro usuário).

**E36 — PATCH /notificacoes/lidas** (Autenticado)
- Saída: `204`. Erros: 401.

### 2.7 Denúncias e certificados

**E37 — POST /eventos/{id}/denuncias** (Autenticado) · RN19
- Entrada: `{ motivo, descricao? }`. Saída: `201`.
- `motivo` ∈ {EVENTO_FALSO, GOLPE, CONTEUDO_IMPROPRIO, SPAM, OUTRO}; `descricao` ≤ 500, obrigatória se OUTRO.
- Erros: 400; 401; 404; 409 `DENUNCIA_EXISTENTE`; 422 `NAO_PODE_DENUNCIAR_PROPRIO_EVENTO`; 429.

**E38 — GET /inscricoes/{id}/certificado** (Dono) · RN21
- Saída: `200` arquivo `application/pdf`. Emite na primeira chamada. Erros: 401; 403; 404; 422 `CERTIFICADO_INDISPONIVEL`.

**E39 — GET /certificados/{codigo}/validacao** (Público) · RN21
- Saída: `200 { valido: true, participante, evento, dataEvento, cargaHoraria, emitidoEm }`. Código ≤ 20 alfanuméricos.
- Erros: 404 `CERTIFICADO_NAO_ENCONTRADO`; 429.

## 3. Rate limiting (resumo, ver [../seguranca.md](../seguranca.md))

Por IP e por usuário: login (5/min), envio de código SMS (RN18), denúncias e validação de certificado. O webhook (E31) **não** passa pelo rate limiting.
