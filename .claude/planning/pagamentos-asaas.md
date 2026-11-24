# Pagamentos reais com Asaas (sandbox): plano

Decisões da equipe em 2026-10-28:
- CNPJ no **usuário**, obrigatório para criar evento, gratuito ou pago (RN24, **feito**).
- O dinheiro da inscrição vai **direto para o organizador**.
- Reembolso com o valor **retido** (Conta Escrow) até o evento acontecer.
- Demonstração no **sandbox** do Asaas.

Este documento é o plano. Feito: Fase 1a (CNPJ) e o adaptador da Fase 1b (`GatewayPagamentoAsaas`, testado só com HTTP falso). O script da Fase 0 ([docs/asaas](../../docs/asaas/README.md)) rodou em 2026-11-24 com uma conta sandbox **PF**: Q4 respondida, Q5 parcial, Q1/Q2/Q3/Q6 bloqueadas até haver conta sandbox **PJ** (seção 6). O que a documentação diz da Conta Escrow está na seção 7.

## 1. Modelo

```
Participante ──Pix──► Cobrança na conta raiz do Viteme ──split 100%──► Subconta do organizador (CNPJ)
                                                                       └─ Conta Escrow: valor bloqueado
                                                                          até "encerrado + 2 dias"
```

- **Conta raiz (Viteme).** No sandbox, uma conta de teste. Em produção, precisaria ser PJ, e a criação de subcontas pela API depende do modelo BaaS acordado com o gerente do Asaas (período de avaliação regulatória). **Fora do escopo da Entrega 3.**
- **Subconta por organizador.** Criada com `POST /v3/accounts` usando o CNPJ verificado (RN24). Recebe `walletId` e chave de API próprios. O titular envia os documentos pelo `onboardingUrl` e o Asaas aprova ou rejeita.
- **Cobrança.** Criada na conta raiz, com `split: [{ walletId, percentualValue: 100 }]`. Viteme não cobra taxa (spec); a tarifa do Asaas sai do valor líquido. Referência externa = `pagamento.id` (como no stub).
- **Escrow.** Configuração padrão para todas as subcontas (`daysToExpire` como trava, ex.: 60 dias). O Viteme libera cada garantia com `POST /v3/escrow/{id}/finish` quando o evento encerra + 2 dias.
- **Reembolso.** Os fluxos já existem (RN14 cancelamento do evento, RN15 cancelamento do participante, RN23 aprovação tardia). O `IGatewayPagamento.SolicitarEstornoAsync` passa a chamar `POST /v3/payments/{id}/refund`, que **desfaz o split junto**. Com o valor ainda em garantia, o estorno não depende de o organizador ter saldo.

## 2. Perguntas a responder no sandbox (Fase 0)

| # | Pergunta | Por que importa |
|---|---|---|
| Q1 | O valor que chega à subconta **pelo split** fica retido pela Conta Escrow? A documentação fala de "valores recebidos pela subconta" | Se não ficar, a cobrança precisa ser criada pela própria subconta, com a chave dela (guardar chave de API cifrada por organizador) |
| Q2 | Quem pode chamar `/escrow/{id}/finish`: a conta raiz ou só a subconta? Onde vem o id da garantia (objeto `escrow` da cobrança)? | Define se o Viteme precisa guardar a chave de cada subconta |
| Q3 | Campos obrigatórios da subconta PJ (`companyType`, endereço, CEP, `incomeValue`, telefone...) | Define o formulário "Dados de recebimento". A BrasilAPI devolve o endereço do CNPJ e pode preencher parte |
| Q4 | O **pagador** precisa de CPF para cobrança Pix? Hoje `usuario` não tem CPF (P1) | Se precisar, pedir o CPF só no momento de pagar (validado, minimização LGPD) |
| Q5 | Cabeçalho e formato do webhook (`asaas-access-token`, eventos `PAYMENT_RECEIVED`, `PAYMENT_CONFIRMED`, `PAYMENT_OVERDUE`, `PAYMENT_REFUNDED`...) e eventos de status da subconta | Implementar `ValidarAssinaturaWebhook`/`InterpretarWebhook` (P2) |
| Q6 | No sandbox a subconta é aprovada sozinha ou precisa simular o onboarding? | Roteiro da demonstração |

## 3. Fases

### Fase 0 — Prova no sandbox (spike)
Conta sandbox, chave de API, criação de uma subconta, Conta Escrow padrão, cobrança Pix com split, pagamento simulado, estorno, liberação de garantia, webhooks recebidos. Guardar as respostas reais em `docs/evidencias/` e responder Q1–Q6 aqui. **Sem código de produção.**

### Fase 1a — CNPJ do organizador ✅ (2026-10-28)
E40 `PUT /usuarios/me/cnpj`, RN24, migration `CnpjUsuario`, consulta na BrasilAPI. Ver [decisoes.md](decisoes.md) D50.

### Fase 1b — Adaptador `GatewayPagamentoAsaas`
- Implementa `IGatewayPagamento` (o contrato já existe; o stub continua para testes e para desenvolvimento offline).
- `Gateway:Provedor = Stub | Asaas`; `Asaas:UrlBase` (`https://api-sandbox.asaas.com/v3`), `Asaas:ChaveApi` e `Asaas:TokenWebhook` só em variável de ambiente.
- Cliente do pagador: buscar ou criar por `externalReference = usuario.id`.
- Cobrança `billingType: PIX`, vencimento = fim da reserva, split para o `walletId` do organizador; QR Code Pix por `GET /payments/{id}/pixQrCode` (o frontend mostra no lugar do simulador).
- Estorno idempotente (P30): consultar a cobrança antes de pedir de novo.
- Webhook: token em cabeçalho comparado em tempo constante (P2); mapear eventos → `StatusPagamento`.
- Reconciliação (P29): a tarefa de reservas consulta cobranças pendentes perto do vencimento.
- Testes com `HttpMessageHandler` falso (mesmo padrão de `ConsultaCnpjBrasilApi`) + roteiro manual no sandbox.

### Fase 2 — Conta de recebimento do organizador (subconta)
- Entidade `ContaRecebimento` (usuário, id da conta Asaas, `walletId`, situação `PENDENTE | APROVADA | REJEITADA`, `onboardingUrl`, chave da subconta cifrada **só se Q1/Q2 exigirem**).
- E41 `POST /usuarios/me/conta-recebimento` (cria a subconta com o CNPJ) e E42 `GET /usuarios/me/conta-recebimento` (situação e link do onboarding). Webhook de situação da conta.
- **RN25 (proposta):** evento **pago** só é publicado com conta de recebimento APROVADA; evento gratuito exige só o CNPJ (RN24).
- Depois de criada a subconta, o CNPJ do usuário fica travado (trocar exigiria nova subconta).

### Fase 3 — Garantia (escrow) e liberação
- Guardar em `pagamento` o id da garantia e `garantia_liberada_em`.
- **RN26 (proposta):** o valor fica retido até o evento ser ENCERRADO + 2 dias; uma tarefa periódica libera as garantias vencidas (`finish`). Evento cancelado → estorno (RN14), garantia nunca liberada.
- `daysToExpire` do Asaas maior que a duração máxima de um evento + 2 dias, como trava caso a tarefa falhe.
- Habilitar a garantia **antes** do primeiro recebimento de cada organizador (só recebimentos posteriores à habilitação ficam retidos): usar o padrão para todas as subcontas (`POST /v3/accounts/escrow`). A garantia tem **mensalidade** (seção 7): decidir quem paga (`isFeePayer`).
- Depois da liberação não há estorno pelo Viteme (o evento já aconteceu); RN15 continua valendo até 24 h antes do início.

### Fase 4 — Frontend
- Perfil: seção "Recebimentos" (situação da subconta, botão para o onboarding).
- Pagamento: QR Code Pix real e "copia e cola"; o simulador sai quando `Gateway:Provedor = Asaas`.
- Gestão do evento: total recebido, retido e liberado.

## 4. Definição de pronto (para a demonstração)
Fluxo completo no sandbox: organizador com CNPJ → subconta aprovada → evento pago publicado → participante paga Pix → valor aparece **retido** na subconta → (a) participante cancela e recebe estorno, ou (b) evento encerra e a garantia é liberada. Webhooks reais, sem simulador. Testes automatizados com handler falso; roteiro manual registrado em `docs/evidencias/`.

## 5. Fontes
- Conta Escrow: https://docs.asaas.com/docs/introducao-conta-escrow · https://docs.asaas.com/docs/bloqueio-dos-valores · https://docs.asaas.com/reference/encerrar-garantia-da-cobranca-na-conta-escrow · https://docs.asaas.com/docs/habilitando-a-conta-escrow-para-as-subcontas
- Split e estorno: https://docs.asaas.com/docs/split-de-pagamentos · https://docs.asaas.com/docs/split-em-cobrancas-avulsas · https://docs.asaas.com/reference/estornar-cobranca-com-dados-resumidos-na-resposta
- Subcontas: https://docs.asaas.com/docs/criacao-de-subcontas · https://docs.asaas.com/docs/detalhamento-do-fluxo-de-aprova%C3%A7%C3%A3o-de-subcontas · https://docs.asaas.com/docs/duvidas-frequentes-subcontas

## 6. Respostas da Fase 0 (execução de 2026-11-24 no sandbox)

Evidência: [docs/evidencias/asaas-fase0-2026-11-24.json](../../docs/evidencias/asaas-fase0-2026-11-24.json), gerada por [docs/asaas/fase0-sandbox.ps1](../../docs/asaas/README.md). A conta usada é de **pessoa física**.

| # | Resposta | Situação |
|---|---|---|
| Q4 | **O CPF/CNPJ do pagador é obrigatório para cobrar.** O Asaas aceita criar o *cliente* sem documento (HTTP 200), mas a *cobrança* Pix para esse cliente é recusada: HTTP 400 "Para criar esta cobrança é necessário preencher o CPF ou CNPJ do cliente". O Viteme precisa coletar e validar o CPF do pagador (só ao pagar, LGPD) antes de produção (P1) | **Respondida** |
| Q3 | Não testada: `POST /accounts` respondeu **403** "Contas de pessoa física (CPF) não podem criar subcontas. Apenas contas de pessoa jurídica (CNPJ)". A conta raiz do Viteme **tem de ser PJ**, também no sandbox, para testar subcontas, split e escrow | Bloqueada |
| Q6 | Idem Q3 (sem subconta não há aprovação para observar) | Bloqueada |
| Q1, Q2 | Idem: dependem de subconta com escrow | Bloqueada |
| Q5 | Pagamento simulado (`POST /sandbox/payment/{id}/confirm`) levou a cobrança a `RECEIVED` (e não `CONFIRMED`), coerente com o mapeamento do adaptador. O envio do webhook não foi testado: nenhum webhook foi cadastrado no painel | Parcial |

Outras constatações:
- `dueDate` por data foi aceito; `externalReference` e a busca por ela funcionam (base da idempotência do adaptador).
- `netValue` 9,01 para `value` 10,00: a tarifa do Pix (R$ 0,99) sai do valor líquido, como o plano previa.
- `invoiceUrl` vem na criação da cobrança, mesmo sem chave Pix cadastrada.
- `GET /payments/{id}/pixQrCode` responde **400** "Você não possui uma chave Pix cadastrada para recebimentos". Para o QR Code próprio (Fase 4) a conta raiz precisa de uma chave Pix.
- O **estorno** logo após o recebimento respondeu **400 "Saldo insuficiente"**: o valor recebido ainda não está no saldo da conta do sandbox (saldo 0). O adaptador sobe `HttpRequestException` e o `EstornoService` repete a cada minuto (D30), então o estorno só passa quando o valor creditar. Confirmar no sandbox adicionando saldo à conta.
- O `User-Agent` enviado (`Viteme-Fase0/1.0`) não causou erro; se ele é obrigatório continua sem confirmação.

## 7. Conta Escrow e conta sandbox PJ: o que a documentação diz (consulta de 2026-11-24)

Lido na documentação, **ainda não testado** no sandbox.

- **Habilitação sem suporte.** A conta raiz habilita a garantia das subcontas pela API, sem liberação prévia do Asaas: por subconta `POST /v3/accounts/{id}/escrow` (`enabled`, `isFeePayer`, `daysToExpire`) ou como padrão para todas, atuais e futuras, `POST /v3/accounts/escrow`. Conferência: `GET /v3/accounts/{id}/escrow` e `GET /v3/accounts/escrow`.
- **Só vale para recebimentos depois da habilitação**; os anteriores não ficam retidos. Cada subconta do fluxo precisa estar com a garantia habilitada.
- **Mensalidade.** Cobrada a partir do dia seguinte à ativação; `isFeePayer` define se paga a subconta ou a conta raiz. Custo não previsto no plano: decidir antes de produção.
- **Desabilitar encerra todas as garantias da subconta**; reabilitar só depois que as garantias em liberação forem encerradas.
- **Consulta da garantia de uma cobrança** (`GET /v3/payments/{id}/escrow`) usa a **chave da própria subconta**, e não há webhook de mudança da garantia. Indício de que o Viteme vai guardar a chave de cada subconta, cifrada (a condição "só se Q1/Q2 exigirem" da Fase 2).
- **Sandbox.** A página "O que pode ser testado" não cita a Conta Escrow: confirmar chamando `POST /v3/accounts/escrow` com a conta sandbox PJ (entra na nova rodada da Fase 0).
- **Conta sandbox PJ (P55).** O sandbox é separado da produção e aprova a conta sozinho quando os dados obrigatórios estão corretos (nome sem números ou caracteres especiais). Limites: 20 subcontas por dia; a partir da primeira subconta, período de avaliação com até 10 subcontas de titulares diferentes, R$ 2.000 em cobranças por subconta e 60 dias. A chave de API da subconta só aparece na criação. O onboarding por link é simulado e o teste dele depende de liberação do suporte. Não confirmado: se o sandbox aceita CNPJ só com dígitos válidos ou exige CNPJ real.

Fontes: [Introdução à Conta Escrow](https://docs.asaas.com/docs/introducao-conta-escrow) · [Habilitando a Conta Escrow para as subcontas](https://docs.asaas.com/docs/habilitando-a-conta-escrow-para-as-subcontas) · [Bloqueio dos valores](https://docs.asaas.com/docs/bloqueio-dos-valores) · [O que pode ser testado](https://docs.asaas.com/docs/o-que-pode-ser-testado) · [FAQ do Sandbox](https://docs.asaas.com/docs/faq-sandbox) · [Criar subconta](https://docs.asaas.com/reference/criar-subconta) · [Creating subaccounts](https://docs.asaas.com/docs/creating-subaccounts)
