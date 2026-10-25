# Pendências da API

Situação em **2026-10-23**, após a etapa 6 (281 testes passando, cobertura de `Application/Services` em 93,4%).
Este documento lista só o que **ainda falta ou foi aceito como risco**. O histórico completo (inclusive o que já foi resolvido) está em [`.claude/planning/pendencias.md`](../.claude/planning/pendencias.md); as decisões, em [`.claude/planning/decisoes.md`](../.claude/planning/decisoes.md).

Os códigos (P1, P2…) são os mesmos nos dois arquivos.

**Prioridade:** 🔴 bloqueia a demonstração ou a publicação · 🟡 deve ser feita antes de entregar · 🟢 opcional.

## 1. Bloqueiam a entrega

| Prioridade | Cód. | Pendência | Quem decide | Quando |
|---|---|---|---|---|
| 🔴 | P10 | **Frontend** é exigido pela Entrega 3. Falta definir prazo, responsável e stack (a spec indica React + TypeScript + Vite) e se fica em pasta `viteme-web` neste repositório ou em outro | Equipe | Antes da etapa 7 |
| 🔴 | P12 | **Autoria no Git:** os commits saem como "Claude". A Entrega 3 pede histórico que mostre cada integrante: cada um precisa commitar com identidade própria (branches + PRs) | Equipe | Desde já |
| 🔴 | P14 | **Pagamento e SMS são simulados** (stub). Declarar isso na apresentação ou usar o sandbox do Asaas (o que torna P1 e P2 obrigatórias) | Equipe | Antes da demonstração |
| 🔴 | P32 | O **simulador de pagamento** precisa estar **desligado** em produção (`Gateway__Stub__HabilitarSimulador=false`); remover quando o Asaas entrar | Dev | Antes de publicar |

## 2. Integrações reais (dependem de conta e escolha de fornecedor)

| Prioridade | Cód. | Pendência | Quem decide | Quando |
|---|---|---|---|---|
| 🟡 | P1 | O Asaas exige **CPF/CNPJ do pagador**? O usuário não tem CPF. Se exigir: novo campo, validação e cuidados de LGPD | Equipe | Antes do adaptador Asaas |
| 🟡 | P2 | O webhook do Asaas autentica por **token em cabeçalho**, não necessariamente HMAC. Confirmar na documentação (a interface `ValidarAssinaturaWebhook` já abstrai) | Dev | Antes do adaptador Asaas |
| 🟡 | P29 | **Reconciliação com o gateway:** a situação do pagamento só muda por webhook. Se um aviso se perder, o participante paga e a reserva expira. Usar `ConsultarCobrancaAsync` para conferir cobranças pendentes perto do vencimento | Dev | Antes do adaptador Asaas |
| 🟡 | P30 | O estorno é pedido ao gateway antes de gravar `ESTORNADO`. Se o banco falhar depois, o próximo ciclo pede de novo: o adaptador real deve enviar **chave de idempotência** (ex.: id do pagamento) | Dev | Adaptador Asaas |
| 🟡 | P4 / P39 | **Provedor de SMS real** (pay-per-use: AWS SNS, Zenvia, Comtele, Twilio). O fluxo (fila, 4 tentativas, descarte) já está pronto e testado; falta o adaptador. Com AWS SNS: sandbox, sender ID no Brasil e limite de gasto | Equipe | Opcional |

## 3. Hospedagem e operação

| Prioridade | Cód. | Pendência | Quem decide | Quando |
|---|---|---|---|---|
| 🟡 | P11 | Onde publicar API, frontend e banco em HTTPS e como a banca acessa o Supabase. O cookie agora é `SameSite=None` (não exige mesmo site), mas navegadores que bloqueiam cookies entre sites podem não restaurar a sessão; o mais seguro é o mesmo domínio registrável. Guia em [deploy.md](deploy.md) | Equipe | Antes da etapa 7 |
| 🟡 | P6 | Ativar `Proxy__ConfiarEmCabecalhosEncaminhados=true` e `Proxy__SaltosConfiaveis=2` (repasse da Cloudflare + Azure) na hospedagem (senão o limite por IP vê o IP do proxy) e, se possível, restringir os proxies confiáveis | Dev/Equipe | Na hospedagem |
| ✅ | P8 | Cookie `SameSite=None` com proteção CSRF em E03/E04 (cabeçalho `X-Requested-With` e `Origin`); origem configurável por `Cors__OrigemFrontend` | Dev | Resolvida |
| 🟢 | P22 | Em sessão nova de desenvolvimento, subir o PostgreSQL local (`pg_ctlcluster 16 main start`) e definir `VITEME_TESTES_DB` | Dev | — |

## 4. Depende do frontend

| Prioridade | Cód. | Pendência | Quem decide |
|---|---|---|---|
| 🟡 | P31 | Mostrar **"estorno em andamento"** quando o pagamento está APROVADO mas a inscrição já foi cancelada/expirada | Equipe |
| 🟡 | P38 | Definir o domínio real e criar a **tela pública de validação de certificado** (o PDF imprime `Certificados:UrlValidacao`) | Equipe |
| 🟡 | P40 | Renovar o token **uma vez só** e compartilhar entre abas: reuso de refresh já revogado derruba todas as sessões | Equipe |
| 🟡 | P42 | **Content-Security-Policy** fica no frontend/host estático (a API já envia `nosniff`, `X-Frame-Options`, `Referrer-Policy`, `no-store`) | Equipe |
| 🟢 | P26 | Filtros de data do mural usam o dia em **UTC**; eventos à noite podem cair no dia seguinte (Brasília é UTC−3). Tratar no frontend ou aceitar fuso na consulta | Equipe |

## 5. Melhorias opcionais

| Prioridade | Cód. | Pendência | Quem decide |
|---|---|---|---|
| 🟢 | P33 | Avisar o participante quando a **reserva expira** (hoje só o mural e "Minhas inscrições" refletem) | Equipe |
| 🟢 | P37 | Lembrar de **pagar** quem tem inscrição pendente (o lembrete hoje vai só para inscrições confirmadas e interessados sem inscrição) | Equipe |

## 6. Riscos aceitos (sem ação prevista)

| Cód. | Risco | Por que foi aceito |
|---|---|---|
| P16 | Rate limiting em memória: com mais de uma instância os limites são por instância | A API é um monólito de instância única |
| P17 | O JWT (15 min) continua válido depois do logout | Token sem estado; a spec aceita. O refresh é revogado |
| P19 | `POST /auth/cadastro` revela se o e-mail já existe (409) | Exigido pela spec; mitigado pelo limite por IP |
| P25 | Avisos de alteração/cancelamento são gravados um a um na mesma transação | Suficiente para eventos de faculdade; usar lotes/fila só se escalar |
| P28 | E16, E17 e E21 retornam listas sem paginação | A spec não pede |
| P34 | Pagamentos criados no mesmo instante teriam ordem indefinida em "mais recente" | Na prática há segundos entre tentativas |
| P35 | Pagamento aprovado com a reserva já vencida é estornado (RN23), mesmo havendo vaga | Regra da spec |
| P36 | O PDF só tem caracteres Latin-1 (alfabeto cirílico, emojis saem como `?`) | Público é de língua portuguesa |
| P41 | `Infrastructure/Tarefas` com 27,7% de cobertura (workers desligados nos testes) | A lógica que eles chamam tem 90%+ |

## 7. Resolvidas na etapa 6 (referência)

P15 (corrida na renovação do refresh), P18 (JWT com `IRelogio`), P20 (limite de códigos por telefone) e P21 (cobertura medida). Antes disso: P3 (PDF próprio), P24 (RN19), P27 (canal SMS), P13 (Swagger), P7, P9.

## 8. Como manter este arquivo

- Ao resolver uma pendência: remova-a daqui, marque como resolvida em `.claude/planning/pendencias.md` e cite a decisão (Dxx) ou o teste (CTxx).
- Ao descobrir uma nova: acrescente em `.claude/planning/pendencias.md` (próximo número Pxx) e, se ainda estiver aberta, copie para a seção adequada daqui.
