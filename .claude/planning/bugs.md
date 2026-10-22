# Registro de bugs

Registrar **no momento em que o bug é encontrado**, não no fim. A banca pede os principais bugs e como foram corrigidos.

| # | Data | Onde (endpoint/tela/camada) | Sintoma | Causa | Correção | Commit | Caso de teste |
|---|---|---|---|---|---|---|---|
| 1 | 2026-10-22 | `POST /auth/cadastro` (qualquer 400 de validação) | A resposta 400 saía com `Content-Type: application/json` em vez de `application/problem+json` (RFC 9457), embora o corpo estivesse certo | O `InvalidModelStateResponseFactory` devolvia um `ObjectResult`; o `[Produces("application/json")]` dos controllers reescreve os `ContentTypes` desse resultado | O factory passou a devolver um `IActionResult` próprio que escreve o ProblemDetails direto na resposta (`RespostaProblema.EscreverAsync`), mantendo `application/problem+json` | 515361a | CT03 |

## Modelo de entrada

- **Sintoma:** o que se observou (entrada, resposta, erro).
- **Causa raiz:** por que aconteceu.
- **Correção:** o que mudou, e por que resolve.
- **Como evitar:** teste adicionado, validação, regra.
