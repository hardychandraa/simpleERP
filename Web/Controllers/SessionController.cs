using Microsoft.AspNetCore.Mvc;

namespace SimpleERP.Web.Controllers;

/// <summary>
/// Keeps an active Admin signed in while they are typing a long form (R7, HC 2026-10-16): the
/// page pings this every few minutes only if the mouse or keyboard was used since the last
/// ping, so "idle" means nobody at the keyboard, not "no page loaded". Signed-in users only
/// (fallback policy); it changes nothing, so it needs no antiforgery token.
/// </summary>
[ApiController]
[Route("api/session")]
public class SessionController : ControllerBase
{
    [HttpGet("ping")]
    public IActionResult Ping() => NoContent();
}
