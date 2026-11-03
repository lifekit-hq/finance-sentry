using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Auth.Application.Commands;
using FinanceSentry.Modules.Auth.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace FinanceSentry.Modules.Auth.API.Controllers;

/// <summary>The signed-in person's own account. There is no user id in the route: every action is on the caller.</summary>
[ApiController]
[Authorize]
[Route("account")]
public class AccountController(
    IQueryHandler<GetAccountClosureQuery, AccountClosureOptionsDto> closureOptionsHandler,
    ICommandHandler<CloseAccountCommand, AccountClosureDto> closeAccountHandler,
    AccountDataExportWriter exportWriter,
    TimeProvider clock,
    ILogger<AccountController> logger) : ControllerBase
{
    [HttpGet("closure")]
    public async Task<IActionResult> ClosureOptions(CancellationToken ct)
        => Ok(await closureOptionsHandler.Handle(new GetAccountClosureQuery(User.RequireUserId()), ct));

    [HttpPost("close")]
    public async Task<IActionResult> Close([FromBody] CloseAccountRequest request, CancellationToken ct)
        => Ok(await closeAccountHandler.Handle(new CloseAccountCommand(User.RequireUserId(), request.ConfirmEmail), ct));

    /// <summary>
    /// The caller's own data as one JSON download: every row the closure purge would erase, minus stored secrets. There
    /// is no user id in the route or the query - the person is the access token's - so there is no way to ask for
    /// someone else's.
    /// </summary>
    [HttpGet("export")]
    public async Task Export(CancellationToken ct)
    {
        var userId = User.RequireUserId();
        Response.ContentType = "application/json";
        Response.Headers.ContentDisposition = $"attachment; filename=\"finance-sentry-export-{clock.GetUtcNow():yyyy-MM-dd}.json\"";
        Response.Headers.CacheControl = "no-store";

        logger.LogInformation("Data export requested by {UserId}", userId);
        await exportWriter.WriteAsync(userId, Response.Body, ct);
    }
}
