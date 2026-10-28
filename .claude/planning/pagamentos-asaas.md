# Pagamentos reais com Asaas (sandbox): plano

Decisões da equipe em 2026-10-28:
- CNPJ no **usuário**, obrigatório para criar evento, gratuito ou pago (RN24, **feito**).
- O dinheiro da inscrição vai **direto para o organizador**.
- Reembolso com o valor **retido** (Conta Escrow) até o evento acontecer.
- Demonstração no **sandbox** do Asaas.

Este documento é o plano. Nada daqui foi implementado ainda, exceto a Fase 1a (CNPJ).

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
