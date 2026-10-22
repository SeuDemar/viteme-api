using Microsoft.AspNetCore.Mvc;

namespace Viteme.Api.Controllers;

[ApiController]
[Route("api/v1/saude")]
public class SaudeController : ControllerBase
{
    [HttpGet]
    public IActionResult Obter() => Ok(new { status = "ok" });
}
