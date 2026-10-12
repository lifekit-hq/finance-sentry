namespace FinanceSentry.Modules.Companion.API.Controllers;

using System.Diagnostics;
using FinanceSentry.Core.Api;
using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Companion.API.Requests;
using FinanceSentry.Modules.Companion.API.Responses;
using FinanceSentry.Modules.Companion.Application.Commands;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

/// <summary>
/// Problem reports from the signed-in person (owner or member). The report is saved first and forwarded to the fleet
/// inbox by a job, so a missing or slow relay never fails the request.
/// </summary>
[ApiController]
[Authorize]
[Route("feedback")]
public class ProblemReportsController(
    ICommandHandler<SubmitProblemReportCommand, SubmittedProblemReport> submit) : ControllerBase
{
    [HttpPost]
    [EnableRateLimiting(RateLimitingPolicies.ProblemReport)]
    public async Task<IActionResult> Submit([FromBody] SubmitProblemReportRequest body, CancellationToken ct)
    {
        var role = User.IsInRole(AuthRoles.Owner) ? "owner" : "member";
        // The request's W3C trace id: the same value every log line of this request carries as TraceId.
        var traceId = Activity.Current?.TraceId.ToHexString() ?? HttpContext.TraceIdentifier;
        var result = await submit.Handle(
            new SubmitProblemReportCommand(
                User.RequireUserId(), role, body.Kind, body.Text, body.Route, body.AppVersion, body.Device,
                body.Os, body.Browser, traceId),
            ct);
        return Accepted(new ProblemReportAcceptedDto(result.Reference));
    }
}
