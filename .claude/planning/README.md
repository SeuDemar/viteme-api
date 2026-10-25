# Viteme API — Planejamento

Base: *Especificação Técnica Viteme* (Uniceve, Engenharia de Software, Maringá/PR, 2026 — Vitor Boer, Vitor Antonio, Luiz Ricardo; Prof. Fabricio), complementada pelas decisões tomadas na conversa de inicialização do projeto.

Este repositório contém **a API**. O frontend (`viteme-web`, React) vem depois, em outro repositório/pasta, mas é exigido pela Entrega 3 (ver [entrega-3.md](entrega-3.md)).

## Índice

| Arquivo | Conteúdo |
|---|---|
| [decisoes.md](decisoes.md) | Decisões da equipe, desvios em relação ao PDF e pontos em aberto |
| [entrega-3.md](entrega-3.md) | Conferência dos requisitos da Entrega 3 contra o plano (lacunas e ajustes) |
| [testes-execucao.md](testes-execucao.md) | Registro de execução dos testes (resultado e evidência) |
| [bugs.md](bugs.md) | Registro de bugs e correções |
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
- Etapa 1 concluída: entidades, enums, DbContext, migration inicial (`docs/banco/schema.sql`) e Swagger UI. 64 testes passando.
- Spec lida por completo e transcrita nesta pasta.
- Etapa 2 concluída: autenticação e conta (E01–E09), CT01–CT13 passando; 97 testes.
- Etapa 3 concluída: eventos, programação e colaboradores (E10–E23); 157 testes passando.
- Etapa 4 concluída: inscrições, pagamentos (gateway stub), webhook, estornos e check-in (E24–E31); 229 testes passando.
- Próximo passo: etapa 5 do [roadmap.md](roadmap.md) (interesse, notificações, denúncias e certificados, E32–E39).
