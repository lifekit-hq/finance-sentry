namespace FinanceSentry.API.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

/// <summary>
/// Health check endpoint for monitoring and liveness probes.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("[controller]")]
public class HealthController : ControllerBase
{
    /// <summary>
    /// Get health status of the application.
    /// </summary>
    /// <returns>200 OK with health status</returns>
    [HttpGet]
    public IActionResult GetHealth()
    {
        return Ok(new { status = "healthy", timestamp = DateTime.UtcNow });
    }
}
