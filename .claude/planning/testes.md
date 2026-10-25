# Planejamento dos testes

## 1. Estratégia e ferramentas

| Nível | Ferramentas | Escopo |
|---|---|---|
| Unitário (backend) | xUnit + biblioteca de mocks (Moq ou NSubstitute) | Services e entidades: regras de negócio isoladas, com repositórios, gateway, SMS e relógio simulados |
| Integração (backend) | `WebApplicationFactory` + Testcontainers com PostgreSQL | Endpoints de ponta a ponta com banco real: autenticação, autorização, restrições do banco, concorrência e webhook |
| Frontend (futuro) | Vitest, React Testing Library, MSW | Fora do escopo agora |
| Ponta a ponta (opcional, futuro) | Playwright | Fora do escopo agora |

- O tempo é tratado por `IRelogio`; reserva de 30 min, cancelamento até 24 h antes e validade de código são testados sem esperar.
- **Gateway e SMS são sempre simulados nos testes** (stubs/fakes).
- Testcontainers exige Docker. Onde não houver Docker, rodar só os unitários e deixar os de integração para CI/ambiente que o suporte.
- Os testes de integração não usam o Supabase de verdade.

## 2. Funcionalidades a testar

| Funcionalidade | Prioridade | Casos | Faixa |
|---|---|---|---|
| Autenticação e conta | Alta | 7 | CT01–CT07 |
| Telefone e SMS | Média | 6 | CT08–CT13 |
| Eventos | Alta | 9 | CT14–CT22 |
| Inscrições | Alta | 8 | CT23–CT30 |
| Pagamentos | Alta | 8 | CT31–CT38 |
| Alterações, cancelamento e notificações | Alta | 8 | CT39–CT46 |
| Colaboradores e check-in | Média | 5 | CT47–CT51 |
| Denúncias e certificados | Média | 6 | CT52–CT57 |
| Frontend | Média | 6 | CT58–CT63 *(adiados)* |

## 3. Casos de teste (backend)

Formato: `ID — cenário → resultado esperado` · RN · endpoint.

### 3.1 Autenticação e conta
- **CT01** Cadastro válido (nome, e-mail novo, senha `Senha123`) → 201; senha armazenada como hash · RN01, RN02 · E01
- **CT02** E-mail já cadastrado (maiúsculas diferentes) → 409 `EMAIL_JA_CADASTRADO` · RN01 · E01
- **CT03** Senha fraca (`abcdefgh`, sem número) → 400 com erro no campo senha · RN02 · E01
- **CT04** Login válido → 200 com token de acesso e cookie HttpOnly · RN03 · E02
- **CT05** Senha incorreta → 401 `CREDENCIAIS_INVALIDAS`, mesma mensagem de e-mail inexistente · RN03 · E02
- **CT06** 5 logins inválidos seguidos e um 6º com senha correta → 423 `CONTA_BLOQUEADA` até passarem 15 min · RN03 · E02
- **CT07** Reutilização de refresh token (usar duas vezes) → 1ª 200; 2ª 401 e todos os tokens do usuário revogados · E03

### 3.2 Telefone e SMS
- **CT08** Envio de código (telefone cadastrado, não verificado) → 202; SMS com código de 6 dígitos (stub) · RN18 · E07
- **CT09** Novo código 30 s após o primeiro → 429 `AGUARDE_NOVO_CODIGO` · RN18 · E07
- **CT10** Código correto após 10 min → 422 `CODIGO_EXPIRADO` · RN18 · E08
- **CT11** 6ª tentativa com código errado → 422 `TENTATIVAS_EXCEDIDAS`; código invalidado · RN18 · E08
- **CT12** `aceitaSms: true` com telefone não verificado → 422 `TELEFONE_NAO_VERIFICADO` · RN18 · E09
- **CT13** Troca de telefone (verificado e com aceite) → 200; `telefoneVerificado` e `aceitaSms` viram falso · RN18 · E06

### 3.3 Eventos
- **CT14** Criação com dados válidos → 201; RASCUNHO; criador é organizador · RN04, RN06 · E12
- **CT15** `dataFim` anterior a `dataInicio` → 400 com erro em `dataFim` · RN05 · E12
- **CT16** `inscricaoFim` depois de `dataInicio` → 400 com erro em `inscricaoFim` · RN05 · E12
- **CT17** Publicação válida (completo, data futura) → 200; aparece em `GET /eventos` · RN06 · E14, E10
- **CT18** Publicação com `dataInicio` no passado → 422 `DATA_INICIO_PASSADA` · RN06 · E14
- **CT19** Rascunho invisível a outro usuário → 404; ausente do mural · RN06 · E11, E10
- **CT20** Edição por não organizador (colaborador) → 403 · RN07 · E13
- **CT21** Duas edições com a mesma versão em `If-Match` → 1ª 200; 2ª 412 `VERSAO_DESATUALIZADA` · RN07 · E13
- **CT22** Evento publicado com `dataFim` vencida → tarefa altera o status para ENCERRADO · RN22

### 3.4 Inscrições
- **CT23** Inscrição gratuita → 201 CONFIRMADA com `codigoCheckin` · RN08, RN10 · E24
- **CT24** Inscrição duplicada → 409 `INSCRICAO_EXISTENTE` · RN08 · E24
- **CT25** Evento lotado (capacidade 10, 10 ocupadas) → 422 `SEM_VAGAS` · RN08, RN09 · E24
- **CT26** Concorrência pela última vaga (duas simultâneas, 1 vaga) → apenas uma 201; outra 422 `SEM_VAGAS` · RN09 · E24 *(integração, banco real)*
- **CT27** Após `inscricaoFim` → 422 `INSCRICOES_ENCERRADAS` · RN08 · E24
- **CT28** Organizador tenta se inscrever → 422 `PAPEL_NAO_PERMITE_INSCRICAO` · RN04, RN08 · E24
- **CT29** Cancelamento gratuito 2 dias antes → 200 CANCELADA; vaga liberada · RN15 · E26
- **CT30** Cancelamento 3 horas antes do início → 422 `PRAZO_CANCELAMENTO_ENCERRADO` · RN15 · E26

### 3.5 Pagamentos
- **CT31** Inscrição paga (R$ 50,00) → 201 PENDENTE_PAGAMENTO; cobrança de exatamente 50,00; reserva de 30 min · RN11, RN12 · E24
- **CT32** Webhook assinado com status aprovado → 200; pagamento APROVADO; inscrição CONFIRMADA; notificação enviada · RN11, RN16 · E31
- **CT33** Mesmo webhook aprovado duas vezes → 200 nas duas; processado uma única vez · RN23 · E31
- **CT34** Webhook com assinatura incorreta → 401 `ASSINATURA_INVALIDA`; nada alterado · RN23 · E31
- **CT35** Sem pagamento após 30 min → inscrição EXPIRADA e vaga liberada · RN09, RN11
- **CT36** Webhook aprovado para inscrição EXPIRADA → 200; estorno automático solicitado; inscrição não confirmada · RN23 · E31
- **CT37** Cancelamento com pagamento aprovado, dentro do prazo → 200 com `estornoSolicitado: true` · RN15 · E26
- **CT38** Gateway fora do ar ao criar cobrança → 502 `GATEWAY_INDISPONIVEL`; inscrição EXPIRADA e vaga liberada · RN11 · E24

### 3.6 Alterações, cancelamento e notificações
- **CT39** Alteração de local (publicado, com inscritos, novo local e motivo) → 200; alteração registrada; inscritos e interessados notificados · RN13, RN16 · E13, E17
- **CT40** Alteração de data sem `motivoAlteracao` → 422 `MOTIVO_OBRIGATORIO` · RN13 · E13
- **CT41** Alterar preço com inscrição pendente → 422 `PRECO_BLOQUEADO` · RN13 · E13
- **CT42** Notificação por SMS (inscrito com telefone verificado e aceite) → notificação no site + SMS enviado (stub) · RN16
- **CT43** Inscrito sem aceite de SMS → só notificação no site; nenhum SMS · RN16
- **CT44** Falha no SMS (provedor indisponível) → até 3 novas tentativas; operação de origem concluída normalmente · RN16
- **CT45** Cancelamento do evento com inscrições pagas e gratuitas → todas CANCELADA; estornos solicitados; todos notificados · RN14 · E15
- **CT46** Lembrete (evento começa em 24 h) → inscritos confirmados recebem o lembrete · RN17

### 3.7 Colaboradores e check-in
- **CT47** Adicionar colaborador (cadastrado, não inscrito) → 201; usuário notificado; passa a ver inscritos · RN04, RN07 · E22, E27
- **CT48** Organizador informa o próprio e-mail → 422 `ORGANIZADOR_NAO_PODE_SER_COLABORADOR` · RN04 · E22
- **CT49** Check-in válido (código de inscrição confirmada, no dia) → 200; presença registrada · RN20 · E28
- **CT50** Mesmo código duas vezes → 409 `CHECKIN_JA_REALIZADO` · RN20 · E28
- **CT51** Check-in por participante comum → 403 · RN07 · E28

### 3.8 Denúncias e certificados
- **CT52** Primeira denúncia do usuário → 201 · RN19 · E37
- **CT53** 5ª denúncia de usuários diferentes → evento OCULTO; organizador notificado · RN19 · E37
- **CT54** Evento republicado recebe mais 5 denúncias → CANCELADO; inscrições canceladas; estornos solicitados; todos notificados · RN19, RN14 · E37
- **CT55** Inscrito em evento oculto → 200 com os dados do evento; inscrição indisponível para outros · RN19 · E11, E24
- **CT56** Evento encerrado com carga horária e check-in feito → 200 PDF; código de validação gerado · RN21 · E38, E39
- **CT57** Certificado sem presença (inscrição confirmada, sem check-in) → 422 `CERTIFICADO_INDISPONIVEL` · RN21 · E38

> CT58–CT63 (frontend): validação de formulário, rota protegida, renovação automática de token, erros por campo, selo "Alterado" (72 h) e restauração de sessão — **adiados** com o frontend.

## 4. Rastreabilidade (regra → endpoints → testes)

| Regra | Endpoints | Casos |
|---|---|---|
| RN01 | E01 | CT01, CT02 |
| RN02 | E01 | CT01, CT03, CT58 |
| RN03 | E02 | CT04, CT05, CT06 |
| RN04 | E16, E22 | CT14, CT28, CT47, CT48 |
| RN05 | E12, E13, E18, E19 | CT15, CT16 |
| RN06 | E10, E11, E12, E14 | CT14, CT17, CT18, CT19 |
| RN07 | E13, E18, E19, E20, E21, E22, E23, E27, E28 | CT20, CT21, CT47, CT51 |
| RN08 | E22, E24 | CT23, CT24, CT25, CT27, CT28 |
| RN09 | E10, E11, E24 | CT25, CT26, CT35 |
| RN10 | E24 | CT23 |
| RN11 | E24, E29, E30, E31 | CT31, CT32, CT35, CT38 |
| RN12 | E12, E24, E29 | CT31 |
| RN13 | E13, E17, E18, E19, E20, E32 | CT39, CT40, CT41, CT62 |
| RN14 | E15, E31 | CT45, CT54 |
| RN15 | E26, E31 | CT29, CT30, CT37 |
| RN16 | E34 | CT32, CT39, CT42, CT43, CT44 |
| RN17 | E32 | CT46 |
| RN18 | E06, E07, E08, E09 | CT08–CT13 |
| RN19 | E11, E14, E37 | CT52, CT53, CT54, CT55 |
| RN20 | E28 | CT49, CT50 |
| RN21 | E25, E38, E39 | CT56, CT57 |
| RN22 | tarefa em segundo plano | CT22 |
| RN23 | E31 | CT33, CT34, CT36 |

## 5. Critérios de aceite

1. Todos os casos de **prioridade alta** passando antes de cada entrega.
2. Toda regra de negócio coberta por pelo menos um caso de sucesso ou de erro (matriz acima).
3. Testes executados **automaticamente a cada envio de código** ao repositório (CI), bloqueando a integração se algum falhar.
4. **Cobertura mínima de 70%** das linhas da camada de services.
5. Este plano é uma primeira versão e será atualizado conforme a implementação avançar.

## 6. Lacunas conhecidas na cobertura (a tratar durante a implementação)

Itens sem caso na spec; adicionar quando a etapa correspondente for implementada:
- Expiração/reenvio por tarefas em segundo plano além dos casos citados (idempotência com mais de uma instância).
- Autorização: matriz de acesso (um caso por papel × endpoint sensível).
- E03/E04: logout idempotente; refresh expirado.
- E10: filtros e paginação; `alteradoRecentemente` (72 h).
- E13: capacidade abaixo das vagas ocupadas (`CAPACIDADE_ABAIXO_DAS_VAGAS_OCUPADAS`).
- E27/E28: janela de check-in (2 h antes até o fim) e `FORA_DO_HORARIO_DE_CHECKIN`.
- E31: `VALOR_DIVERGENTE` e `TRANSACAO_DESCONHECIDA`.
