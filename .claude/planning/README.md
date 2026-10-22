# Viteme API — Planejamento

Base: *Especificação Técnica Viteme* (Uniceve, Engenharia de Software, Maringá/PR, 2026 — Vitor Boer, Vitor Antonio, Luiz Ricardo; Prof. Fabricio), complementada pelas decisões tomadas na conversa de inicialização do projeto.

Este repositório contém **somente a API**. O frontend (`viteme-web`, React) fica para outro momento e outro repositório.

## Índice

| Arquivo | Conteúdo |
|---|---|
| [decisoes.md](decisoes.md) | Decisões da equipe, desvios em relação ao PDF e pontos em aberto |
| [pendencias.md](pendencias.md) | Pendências técnicas (P1–P9), responsáveis e etapa em que resolver |
| [arquitetura.md](arquitetura.md) | Camadas, projetos da solution, fluxos, integrações e tarefas em segundo plano |
| [banco/modelo.md](banco/modelo.md) | Convenções, 14 entidades, enums, relacionamentos, integridade e índices |
| [api/convencoes-e-erros.md](api/convencoes-e-erros.md) | Convenções da API, ProblemDetails e códigos de erro |
| [api/regras-de-negocio.md](api/regras-de-negocio.md) | RN01–RN23 |
| [api/endpoints.md](api/endpoints.md) | Os 39 endpoints (E01–E39) com entrada, saída e erros |
| [seguranca.md](seguranca.md) | JWT, autorização por papel no evento, SMS/LGPD, riscos e medidas |
| [testes.md](testes.md) | Estratégia, casos de teste (CT01–CT63), rastreabilidade e critérios de aceite |
| [roadmap.md](roadmap.md) | Ordem de implementação e definição de pronto por etapa |

## Resumo em um parágrafo

Viteme centraliza divulgação, inscrição e acompanhamento dos eventos da faculdade, com avisos de alteração e sem taxa da plataforma. Backend em **.NET 10 LTS (ASP.NET Core)**, monólito modular em camadas inspirado em Clean Architecture, **PostgreSQL no Supabase** via EF Core/Npgsql, autenticação **JWT + refresh token rotativo**, pagamentos por gateway atrás de interface (**Asaas** escolhido, implementação posterior; stub primeiro), notificações no site e por SMS (stub primeiro) e modelo **sem administrador**: o papel (participante, organizador, colaborador) é definido **por evento**.

## Estado atual

- Etapa 0 (scaffold) concluída: solution, 5 projetos, health check, middleware de erros e CI.
- Spec lida por completo e transcrita nesta pasta.
- Próximo passo: etapa 1 do [roadmap.md](roadmap.md) (Domain + DbContext + migration inicial).
