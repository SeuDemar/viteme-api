# Registro de bugs

Registrar **no momento em que o bug é encontrado**, não no fim. A banca pede os principais bugs e como foram corrigidos.

| # | Data | Onde (endpoint/tela/camada) | Sintoma | Causa | Correção | Commit | Caso de teste |
|---|---|---|---|---|---|---|---|
| 1 | 2026-10-22 | `POST /auth/cadastro` (qualquer 400 de validação) | A resposta 400 saía com `Content-Type: application/json` em vez de `application/problem+json` (RFC 9457), embora o corpo estivesse certo | O `InvalidModelStateResponseFactory` devolvia um `ObjectResult`; o `[Produces("application/json")]` dos controllers reescreve os `ContentTypes` desse resultado | O factory passou a devolver um `IActionResult` próprio que escreve o ProblemDetails direto na resposta (`RespostaProblema.EscreverAsync`), mantendo `application/problem+json` | 515361a | CT03 |
| 2 | 2026-10-23 | `GET /eventos/{id}/colaboradores` (E21) | Resposta 500 `ERRO_INTERNO` para o organizador, mesmo com o evento e os colaboradores corretos | Consulta EF com `OrderBy` depois de projetar para um record criado por construtor (`new ColaboradorListado(...)`); o EF não consegue traduzir essa ordenação para SQL | A ordenação passou para antes da projeção (consulta em sintaxe de query: `orderby` antes do `select`) | etapa 3 | CT47 |
| 3 | 2026-10-23 | `GET /inscricoes/{id}/certificado` (E38) | Resposta 500 `ERRO_INTERNO` ao gerar o PDF | `CultureInfo.GetCultureInfo("pt-BR")` lança `CultureNotFoundException` quando o runtime roda em modo de globalização invariante (comum em contêineres) | Datas formatadas com `CultureInfo.InvariantCulture` e formato `dd/MM/yyyy` explícito; nenhuma dependência de cultura instalada | etapa 5 | CT56 |

## Modelo de entrada

- **Sintoma:** o que se observou (entrada, resposta, erro).
- **Causa raiz:** por que aconteceu.
- **Correção:** o que mudou, e por que resolve.
- **Como evitar:** teste adicionado, validação, regra.
