using Microsoft.AspNetCore.Mvc;

namespace Viteme.Api.Controllers;

[ApiController]
[Route("api/v1/saude")]
[Produces("application/json")]
public class SaudeController : ControllerBase
{
    /// <summary>Verifica se a API está no ar.</summary>
    /// <response code="200">A API está respondendo.</response>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Obter() => Ok(new { status = "ok" });
}
