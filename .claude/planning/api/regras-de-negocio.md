# Regras de negócio (RN01–RN24)

Aplicadas nos **services**. Cada regra está ligada a endpoints e casos de teste em [../testes.md](../testes.md) (matriz de rastreabilidade).

| Cód. | Tema | Regra |
|---|---|---|
| RN01 | Cadastro | Cada e-mail pode estar associado a uma única conta (comparação sem diferenciar maiúsculas e minúsculas) |
| RN02 | Cadastro | A senha deve ter no mínimo 8 caracteres, com pelo menos uma letra e um número |
| RN03 | Login | Após 5 tentativas de login inválidas seguidas, a conta fica bloqueada por 15 minutos |
| RN04 | Papéis | O papel é definido por evento: quem cria o evento é o organizador; quem o organizador adiciona é colaborador; os demais usuários autenticados são participantes. O organizador não pode ser colaborador do próprio evento |
| RN05 | Evento | A data de término deve ser posterior à de início; o período de inscrições deve terminar até o início do evento; as atividades da programação devem estar dentro do período do evento |
| RN06 | Evento | Todo evento é criado como RASCUNHO e só aparece no mural quando PUBLICADO. Para publicar, os campos obrigatórios devem estar preenchidos e a data de início deve ser futura |
| RN07 | Permissões | Somente o organizador edita, publica, cancela o evento e gerencia colaboradores e programação. O colaborador pode consultar os inscritos e realizar o check-in |
| RN08 | Inscrição | A inscrição só é permitida se o evento estiver PUBLICADO, dentro do período de inscrições, com vaga disponível, e se o usuário não for organizador nem colaborador do evento nem tiver inscrição ativa nele |
| RN09 | Vagas | As vagas ocupadas são as inscrições CONFIRMADA mais as PENDENTE_PAGAMENTO com reserva válida. Capacidade não informada significa vagas ilimitadas |
| RN10 | Inscrição | Em evento gratuito, a inscrição é CONFIRMADA no momento da solicitação |
| RN11 | Pagamento | Em evento pago, a inscrição fica PENDENTE_PAGAMENTO com reserva de vaga por 30 minutos e só é confirmada quando o gateway informar pagamento APROVADO pelo webhook. A cobrança é criada no gateway com validade igual à da reserva. Reserva vencida torna a inscrição EXPIRADA e libera a vaga |
| RN12 | Pagamento | O Viteme não cobra taxa da plataforma: o valor da cobrança é exatamente o preço do evento no momento da inscrição. A taxa de processamento do gateway, quando existir, é informada ao organizador ao criar um evento pago |
| RN13 | Alteração | Alterar título, data, horário, local ou programação de um evento PUBLICADO exige motivo, gera registro de alteração e notifica inscritos ativos e interessados. O preço não pode ser alterado se houver inscrição paga ou pendente, e a capacidade não pode ficar abaixo das vagas ocupadas |
| RN14 | Cancelamento | Ao cancelar o evento (motivo obrigatório), todas as inscrições ativas passam a CANCELADA, os pagamentos APROVADO têm estorno solicitado ao gateway e todos os envolvidos são notificados. Evento ENCERRADO não pode ser cancelado |
| RN15 | Cancelamento | O participante pode cancelar a própria inscrição até 24 horas antes do início do evento; se houver pagamento aprovado, é solicitado o estorno integral |
| RN16 | Notificação | Toda notificação é registrada no site. Ela também é enviada por SMS se o usuário tiver telefone verificado e aceite de SMS ativo. Uma falha no SMS gera até 3 novas tentativas e não interrompe a operação que a originou |
| RN17 | Lembrete | São enviados lembretes 24 horas antes do início do evento aos inscritos confirmados e 24 horas antes do fim das inscrições aos interessados ainda não inscritos |
| RN18 | Telefone | O código de verificação tem 6 dígitos, vale por 10 minutos e aceita no máximo 5 tentativas; um novo código só pode ser pedido 60 segundos após o anterior. O aceite de SMS exige telefone verificado e pode ser revogado a qualquer momento; trocar o telefone desfaz a verificação e o aceite |
| RN19 | Denúncia | Cada usuário pode denunciar um evento uma única vez; organizador e colaboradores não denunciam o próprio evento. Com 5 denúncias, o evento PUBLICADO passa a OCULTO: sai do mural e deixa de aceitar inscrições, mas continua visível para os inscritos, que são avisados junto com o organizador. Como não há administrador, o organizador pode editar e republicar **uma única vez**; se o evento receber outras 5 denúncias após a republicação, é cancelado automaticamente, aplicando-se RN14 (inscrições canceladas e estornos solicitados) |
| RN20 | Check-in | O check-in é feito pelo código da inscrição, apenas para inscrição CONFIRMADA, entre 2 horas antes do início e o fim do evento, e não pode ser repetido |
| RN21 | Certificado | O certificado é emitido para inscrição CONFIRMADA com presença registrada, depois que o evento estiver ENCERRADO e se tiver carga horária informada. Cada certificado tem um código de validação público |
| RN22 | Evento | Um evento PUBLICADO passa automaticamente a ENCERRADO após a data de término |
| RN23 | Webhook | O webhook só é processado com assinatura válida e de forma idempotente (a mesma transação e o mesmo status não são processados duas vezes). Pagamento aprovado para inscrição já expirada ou cancelada gera estorno automático |
| RN24 | Organizador | Só cria e publica eventos (gratuitos ou pagos) quem tem CNPJ verificado no perfil: dígitos verificadores válidos, situação ATIVA na Receita Federal e não vinculado a outra conta. Participar e colaborar não exigem CNPJ |

## Notas de implementação

- **Tempo:** toda regra com prazo usa `IRelogio` (testável sem esperar tempo real).
- **RN08/RN09:** contagem de vagas e gravação da inscrição na **mesma transação** com `SELECT ... FOR UPDATE` no evento (RI10).
- **RN11/RN12:** a chamada ao gateway ocorre **fora** da transação, com o id do pagamento como referência externa e chave de idempotência.
- **RN16:** a falha de SMS nunca propaga erro para a operação de origem; o reenvio é feito pela tarefa de 1 minuto.
- **RN19:** contar denúncias *após* `reaberto_em` para decidir entre ocultar e cancelar; guardar a contagem de forma coerente com a republicação.
- **RN24:** a consulta à Receita (BrasilAPI) acontece só ao cadastrar o CNPJ (E40), não a cada evento; reenviar o mesmo CNPJ já verificado não consulta de novo. A publicação também confere o CNPJ, para cobrir rascunhos criados antes da regra.
- **RN23:** idempotência por (`id_transacao_gateway`, status); aprovação tardia → estorno automático e inscrição permanece não confirmada.
- **Papéis (RN04):** nunca vão no JWT; o papel é consultado no banco a cada requisição protegida por política.
