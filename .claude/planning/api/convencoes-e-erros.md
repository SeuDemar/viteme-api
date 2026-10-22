# API — convenções e erros

## 1. Convenções

- URL base `/api/v1` (as rotas nos demais documentos omitem o prefixo).
- JSON, com campos em `camelCase`.
- Datas ISO 8601 em UTC (ex.: `2026-11-05T19:00:00Z`); valores monetários com duas casas decimais.
- Listas paginadas retornam `{ itens, pagina, tamanhoPagina, total }`.
- Endpoints autenticados exigem `Authorization: Bearer <tokenAcesso>`.
- Parâmetros de rota identificador usam a restrição `{id:guid}`, para que rotas fixas como `/eventos/meus` não sejam confundidas com `/eventos/{id}`.
- Erros seguem **ProblemDetails (RFC 9457)** com o campo adicional `codigo` para erros de regra de negócio.

Exemplo:

```json
{
  "type": "https://viteme.app/erros/sem-vagas",
  "title": "Evento sem vagas",
  "status": 422,
  "codigo": "SEM_VAGAS",
  "detail": "Todas as vagas deste evento já foram preenchidas.",
  "traceId": "00-4f1c..."
}
```

## 2. Códigos de resposta de erro

| Status | Quando ocorre |
|---|---|
| 400 Bad Request | Dados de entrada inválidos (formato, campo obrigatório ausente, tamanho). Inclui `errors` com as mensagens por campo |
| 401 Unauthorized | Token ausente, inválido ou expirado; credenciais incorretas; assinatura de webhook inválida |
| 403 Forbidden | Usuário autenticado sem o papel necessário no evento |
| 404 Not Found | Recurso inexistente ou que o usuário não pode ver (não revela a existência de rascunhos) |
| 409 Conflict | Conflito com o estado atual (inscrição já existente, check-in já feito, evento já cancelado) |
| 412 Precondition Failed | Versão informada em `If-Match` desatualizada (edição concorrente) |
| 422 Unprocessable Entity | Dados válidos, mas a operação viola regra de negócio (sem vagas, prazo encerrado) |
| 423 Locked | Conta temporariamente bloqueada por tentativas de login (RN03) |
| 429 Too Many Requests | Limite de requisições excedido (login, envio de código, denúncias) |
| 500 Internal Server Error | Erro inesperado; resposta sem detalhes internos; erro registrado com `traceId` |
| 502 Bad Gateway | Falha de comunicação com o gateway de pagamento |

## 3. Catálogo de códigos de negócio (campo `codigo`)

Extraído dos endpoints; manter sincronizado com [endpoints.md](endpoints.md).

| Status | Códigos |
|---|---|
| 401 | `CREDENCIAIS_INVALIDAS`, `TOKEN_INVALIDO`, `ASSINATURA_INVALIDA` |
| 404 | `EVENTO_NAO_ENCONTRADO`, `USUARIO_NAO_ENCONTRADO`, `INSCRICAO_NAO_ENCONTRADA`, `TRANSACAO_DESCONHECIDA`, `CERTIFICADO_NAO_ENCONTRADO` |
| 409 | `EMAIL_JA_CADASTRADO`, `EVENTO_FINALIZADO`, `EVENTO_JA_CANCELADO`, `JA_COLABORADOR`, `INSCRICAO_EXISTENTE`, `INSCRICAO_JA_CANCELADA`, `CHECKIN_JA_REALIZADO`, `PAGAMENTO_JA_APROVADO`, `DENUNCIA_EXISTENTE` |
| 412 | `VERSAO_DESATUALIZADA` |
| 422 | `TELEFONE_NAO_INFORMADO`, `TELEFONE_JA_VERIFICADO`, `TELEFONE_NAO_VERIFICADO`, `CODIGO_INVALIDO`, `CODIGO_EXPIRADO`, `TENTATIVAS_EXCEDIDAS`, `PRECO_BLOQUEADO`, `CAPACIDADE_ABAIXO_DAS_VAGAS_OCUPADAS`, `MOTIVO_OBRIGATORIO`, `DADOS_INCOMPLETOS`, `DATA_INICIO_PASSADA`, `EVENTO_ENCERRADO`, `ATIVIDADE_FORA_DO_PERIODO`, `ORGANIZADOR_NAO_PODE_SER_COLABORADOR`, `USUARIO_JA_INSCRITO`, `EVENTO_INDISPONIVEL`, `INSCRICOES_ENCERRADAS`, `SEM_VAGAS`, `PAPEL_NAO_PERMITE_INSCRICAO`, `PRAZO_CANCELAMENTO_ENCERRADO`, `INSCRICAO_NAO_CONFIRMADA`, `FORA_DO_HORARIO_DE_CHECKIN`, `RESERVA_EXPIRADA`, `EVENTO_GRATUITO`, `VALOR_DIVERGENTE`, `NAO_PODE_DENUNCIAR_PROPRIO_EVENTO`, `CERTIFICADO_INDISPONIVEL` |
| 423 | `CONTA_BLOQUEADA` |
| 429 | `AGUARDE_NOVO_CODIGO` (e limites de rate limiting em geral) |
| 502 | `GATEWAY_INDISPONIVEL` |
| (acréscimos da implementação) | `NAO_AUTENTICADO` (401), `DADOS_INVALIDOS` (400), `LIMITE_EXCEDIDO` (429), `SMS_INDISPONIVEL` (502), `ERRO_INTERNO` (500). Lista completa em [../decisoes.md](../decisoes.md), seção 6 |

> Observação: a spec lista `TRANSACAO_DESCONHECIDA` com status 404 e `VALOR_DIVERGENTE` com 422 no webhook (E31).
