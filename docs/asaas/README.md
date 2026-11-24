# Asaas: Fase 0 e adaptador

Plano completo: [.claude/planning/pagamentos-asaas.md](../../.claude/planning/pagamentos-asaas.md).

## Estado

| Item | Situação |
|---|---|
| Adaptador `GatewayPagamentoAsaas` (Fase 1b: cliente, cobrança Pix, consulta, estorno, webhook) | Escrito e testado com manipulador HTTP falso (35 testes). **Nunca rodou contra o Asaas de verdade.** |
| Script da Fase 0 ([fase0-sandbox.ps1](fase0-sandbox.ps1)) | Escrito. **Não foi executado**: faltava a chave de API do sandbox. |
| Respostas Q1–Q6 | Em aberto até rodar o script e conferir o painel. |

## Rodar a Fase 0

1. Crie a conta no sandbox (https://sandbox.asaas.com) e gere a chave em *Integrações > Chaves de API*.
2. No PowerShell, defina a chave só na sessão (nunca no chat nem em arquivo):

```powershell
$env:ASAAS_API_KEY = '<chave do sandbox>'
```

3. Rode o fluxo básico (cliente, cobrança Pix, QR Code, pagamento simulado, estorno):

```powershell
./docs/asaas/fase0-sandbox.ps1
```

4. Opcional, cria uma subconta de teste e uma cobrança com split de 100% para ela (Q1–Q3):

```powershell
./docs/asaas/fase0-sandbox.ps1 -ComSubconta
```

O script grava `docs/evidencias/asaas-fase0-AAAA-MM-DD.json` com a chave e qualquer `apiKey` de subconta mascaradas.
Confira o arquivo antes de commitar.

## O que o script responde e o que falta conferir à mão

- **Q4** (CPF do pagador): o passo `Q4-cliente-sem-cpf` mostra se o Asaas aceita cliente sem `cpfCnpj`.
- **Q3** (campos da subconta PJ): o passo `Q3-subconta` devolve os campos exigidos, em caso de erro.
- **Q6** (aprovação da subconta no sandbox): conferir o status da subconta no painel depois do `-ComSubconta`.
- **Q1/Q2** (escrow): habilitar a Conta Escrow na subconta pelo painel/API e ver se o valor do split fica retido; o script só
  cria a cobrança com split e mostra o detalhe. A chamada `finish` da garantia não está no script (depende da resposta de Q1).
- **Q5** (webhook): cadastre um webhook apontando para a API publicada (ou um túnel), com o mesmo token de
  `Gateway__Asaas__TokenWebhook`, e anote os eventos recebidos durante o pagamento simulado.

## Ligar o adaptador (depois da Fase 0)

Variáveis da API (o `__` é o separador de seção):

| Variável | Valor |
|---|---|
| `Gateway__Provedor` | `Asaas` (padrão `Stub`) |
| `Gateway__Asaas__ChaveApi` | chave do sandbox (obrigatória com `Asaas`; a API não sobe sem ela) |
| `Gateway__Asaas__TokenWebhook` | token que você também cadastra no webhook do Asaas (cabeçalho `asaas-access-token`). Não reutilize a chave de API |
| `Gateway__Asaas__CpfCnpjPagadorPadraoSandbox` | CPF de teste válido, usado só no sandbox enquanto o Viteme não coleta o CPF do pagador (P1) |
| `Gateway__Stub__HabilitarSimulador` | `false` (o simulador assina com o segredo do stub e não funciona com o Asaas) |

### Subconta e garantia (Fases 2 e 3, desligadas por padrão)

As chaves já existem no código; ligar só depois do roteiro da seção 8.5 de `.claude/planning/pagamentos-asaas.md`. A API não sobe com uma combinação inválida (ex.: garantia ligada sem subconta, subconta ligada sem a chave de cifragem).

| Variável | Valor |
|---|---|
| `Gateway__Asaas__Subcontas__UrlWebhook` | `https://<api>/api/v1/pagamentos/webhook`: cadastrado em cada subconta nova, com o mesmo `TokenWebhook`, para a situação da conta chegar na hora. Vazio: só a tarefa periódica atualiza |
| `Gateway__Asaas__Subcontas__AprovarNoSandbox` | `true` **só no sandbox**: a API aprova cada subconta logo depois de criá-la (a autoaprovação do painel do sandbox aprova só os dados comerciais e a subconta fica pendente). A API não sobe com ela ligada e a URL de produção |
| `Gateway__Asaas__ChaveCifragemSubcontas` | 32 bytes em base64 (`openssl rand -base64 32`); cifra a chave de API de cada subconta. Obrigatória com `Subcontas__Habilitar=true`. Trocar torna ilegíveis as chaves já guardadas |
| `Gateway__Asaas__Subcontas__Habilitar` | `false` (padrão). `true` liga a conta de recebimento do organizador (E41/E42), o split e a RN25 |
| `Gateway__Asaas__Escrow__Habilitar` | `false` (padrão). `true` liga a garantia (Fase 3); exige `Subcontas__Habilitar=true` |
| `Gateway__Asaas__Escrow__DiasParaExpirar` | `60` (padrão, de 1 a 365): o Asaas expira a garantia sozinho se a tarefa de liberação falhar |
| `Gateway__Asaas__Escrow__SubcontaPagaMensalidade` | `false` (padrão: a conta raiz paga a mensalidade do escrow) ou `true` (a subconta paga, `isFeePayer`). Decisão da equipe |

URL do webhook a cadastrar no Asaas: `POST https://<api>/api/v1/pagamentos/webhook`, eventos de cobrança.

## Limites conhecidos do adaptador

- Sem CPF/CNPJ do pagador (P1/Q4) a cobrança só funciona no sandbox, com o CPF padrão acima. Em produção a criação do cliente falha.
- Subconta/split (Fase 2) e escrow (Fase 3) estão no código, desligados: a cobrança cai na conta raiz até ligar as chaves acima.
  Com o escrow, a cobrança é criada na subconta (o Asaas só retém cobranças da própria subconta). Antes de publicar, rode no
  Supabase `docs/banco/migracoes/20261125120000_CobrancaNaSubconta.sql`.
- Conferência no sandbox PJ: `docs/asaas/fase2-sandbox.ps1` (chamadas soltas, com evidência) e o teste opt-in
  `VITEME_TESTES_ASAAS_SANDBOX=1 dotnet test --filter AsaasSandboxTests` (adaptadores reais; precisa de `ASAAS_API_KEY`).
  No sandbox: habilitar o split no painel (Integrações > Segurança) e aprovar a subconta com `POST /v3/sandbox/myAccount/approve`
  (chave da subconta). Nome de subconta com número ou caractere especial impede a aprovação.
  Os pontos que dependem da conta sandbox PJ estão marcados com `confirmar Qn` em `ContasRecebimentoAsaas.cs`.
- `UrlPagamento` é a `invoiceUrl` do Asaas (página com o QR Code). Mostrar o QR Code e o "copia e cola" no próprio site
  (`GET /payments/{id}/pixQrCode`) é a Fase 4.
- O vencimento do Asaas é por data (dia, horário de Brasília); a reserva do Viteme é por minuto. Uma cobrança paga depois do fim da
  reserva, no mesmo dia, cai na regra de aprovação tardia (RN23), com estorno.
