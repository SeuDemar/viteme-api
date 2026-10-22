# Entrega 3 — conferência de requisitos

Enunciado: transformar o projeto das etapas anteriores em aplicação **funcional**: implementar, integrar e validar. Entregável: API em C#/.NET, banco de dados, **frontend integrado**, autenticação, **testes executados** e **documentação atualizada**.

Legenda: ✅ coberto pelo plano · ⚠️ coberto em parte / precisa de ajuste · ❌ lacuna (não estava no plano)

## 1. Desenvolvimento da API

| Requisito | Situação | Onde / o que falta |
|---|---|---|
| Endpoints REST | ✅ | 39 endpoints, [api/endpoints.md](api/endpoints.md) |
| Controllers, Services, Repositories, Entities, DTOs | ✅ | [arquitetura.md](arquitetura.md); scaffold já criado |
| Regras de negócio | ✅ | RN01–RN23 |
| Validações | ⚠️ | Validadores de DTO previstos, mas a biblioteca não foi decidida (FluentValidation). Decidir na etapa 2 |
| Tratamento de erros | ✅ | Middleware global + ProblemDetails já implementado e testado |
| Integração com banco | ✅ | EF Core + Npgsql, Supabase |
| **Documentação com Swagger** | ⚠️ | Hoje só o OpenAPI nativo (JSON) em desenvolvimento. **O enunciado pede Swagger**: adicionar **Swagger UI** (ex.: Swashbuckle ou Scalar/Swagger UI sobre o OpenAPI), com botão de autenticação Bearer, exemplos e descrição dos erros. Deve estar acessível no ambiente da demonstração, não só em `Development` |

## 2. Banco de dados

| Requisito | Situação | Onde / o que falta |
|---|---|---|
| Criação das tabelas, relacionamentos, chaves | ✅ | [banco/modelo.md](banco/modelo.md) + migration inicial (etapa 1) |
| Persistência, consultas, inclusão/alteração/exclusão | ⚠️ | Exclusão existe só onde a spec permite (atividades E20, colaboradores E23, interesse E33). Para a demonstração, preparar: (a) **script SQL** gerado da migration (`dotnet ef migrations script`) para mostrar as tabelas; (b) **dados de exemplo (seed)** para demo; (c) roteiro de consultas |
| Banco acessível pela banca | ❌ | Definir como a banca roda: projeto Supabase próprio com connection string em segredo, ou instruções para criar o próprio. Documentar no README |

## 3. Autenticação

✅ Coberta: login, geração de token, JWT, controle de acesso por papel no evento, proteção dos endpoints, validação do usuário autenticado ([seguranca.md](seguranca.md)). Etapa 2.

## 4. Frontend — **lacuna principal**

| Requisito | Situação | O que falta |
|---|---|---|
| Frontend nas telas e fluxos da etapa anterior | ❌ | **Combinamos deixar o frontend para depois, mas esta entrega exige frontend integrado.** Entra no plano como etapa própria (ver [roadmap.md](roadmap.md), etapa 7) com a seção 5 do PDF (telas T01–T12, fluxos, formulários, integração com a API) |
| Navegação, formulários, validações, consumo dos endpoints, exibição, erros, fluxo completo | ❌ | Mesma etapa. Fluxo mínimo para a demonstração: cadastro → login → mural → inscrição (gratuita e paga) → minhas inscrições → criar/publicar evento → check-in → certificado |

Cuidados:
- O cookie de refresh usa `SameSite=Strict`: em **desenvolvimento** `localhost` em portas diferentes é o mesmo site (funciona). Em **hospedagem**, frontend e API precisam estar no **mesmo site** (mesmo domínio registrável). Hospedar em domínios diferentes (ex.: duas plataformas gratuitas) quebra a renovação de sessão. Decidir hospedagem antes do frontend.
- O frontend consome o Swagger/OpenAPI da API; gerar o cliente a partir dele reduz erro de integração.

## 5. Testes e validação

| Requisito | Situação | O que falta |
|---|---|---|
| Executar os testes planejados | ⚠️ | Plano tem 63 casos (CT01–CT63). **CT58–CT63 são de frontend**, então voltam ao escopo. Integração (Testcontainers) precisa de Docker no CI/ambiente (P7) |
| Registrar casos executados, resultado, falhas, correções, evidências | ❌ | Não havia onde registrar. Criar [testes-execucao.md](testes-execucao.md) (tabela por CT com resultado/data/evidência) e [bugs.md](bugs.md) (bug, causa, correção, commit). Guardar saída do `dotnet test`/relatório do CI como evidência |
| Principais bugs e correções | ❌ | `bugs.md`, atualizado **durante** o desenvolvimento, não no fim |

## 6. Controle de versão

| Requisito | Situação | O que falta |
|---|---|---|
| Git mostrando evolução e **participação dos integrantes** | ⚠️ | Os commits até aqui saem com autor "Claude". Para a banca identificar cada integrante: cada um commita o que fez com **nome/e-mail próprios**, usar **branches por funcionalidade + PRs** com revisão entre integrantes, commits pequenos e mensagens claras (sem squash que apague autoria). Combinar quem assume cada etapa/área |

## 7. Documentação final

| Item exigido | Situação | Onde |
|---|---|---|
| Descrição da solução | ✅ | README |
| Como executar / instruções de execução | ⚠️ | README básico; completar com passos de banco, segredos, seed e frontend |
| Tecnologias | ✅ | README |
| Estrutura do projeto | ✅ | README |
| Configuração do ambiente | ⚠️ | README lista variáveis; falta passo a passo (Supabase, user-secrets) |
| Endpoints | ✅ | Swagger + [api/endpoints.md](api/endpoints.md) |
| Banco de dados | ✅ | [banco/modelo.md](banco/modelo.md) (+ script SQL) |
| Autenticação | ✅ | [seguranca.md](seguranca.md) |
| Informações para usar a aplicação | ⚠️ | Falta guia de uso (usuários de demonstração, roteiro) |

Observação: esta pasta `.claude/planning/` é material de trabalho. A documentação **final** da entrega deve ficar em `docs/` (e no README), visível para a banca. Montar na etapa 8, reaproveitando estes arquivos.

## 8. Gateway e SMS na demonstração

A spec e a combinação atual deixam pagamento e SMS em **stub**. Isso cumpre o fluxo (inscrição paga, webhook simulado, SMS registrado), mas deve ser **declarado** na apresentação. Se a banca esperar pagamento real, o caminho é o **sandbox do Asaas** (P1 e P2 viram bloqueantes nesse caso). Decisão da equipe.

## Resumo do que mudou no plano

1. Swagger UI passa a ser obrigatório (etapa 2, ajuste no scaffold).
2. Frontend volta ao escopo, como etapa 7.
3. Novos documentos de acompanhamento: [testes-execucao.md](testes-execucao.md) e [bugs.md](bugs.md).
4. Seed de dados e script SQL para a demonstração do banco.
5. Fluxo de Git com participação de cada integrante.
6. Documentação final em `docs/` e README completo (etapa 8).
7. Hospedagem/demonstração: decidir o ambiente (afeta cookie SameSite e acesso da banca ao banco).
