namespace FinanceSentry.API.Controllers;

using FinanceSentry.Core.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

/// <summary>
/// Health check endpoint for monitoring and liveness probes.
/// </summary>
[ApiController]
[EnableRateLimiting(RateLimitingPolicies.Exempt)]
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
