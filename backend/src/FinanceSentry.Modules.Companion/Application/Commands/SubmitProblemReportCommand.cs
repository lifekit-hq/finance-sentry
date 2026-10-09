namespace FinanceSentry.Modules.Companion.Application.Commands;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Companion.Application.Services;
using FinanceSentry.Modules.Companion.Domain;
using FinanceSentry.Modules.Companion.Domain.Exceptions;
using FinanceSentry.Modules.Companion.Domain.Repositories;

/// <summary>Saves a problem report for the signed-in person. Client-supplied fields are cleaned here, not trusted.</summary>
public record SubmitProblemReportCommand(
    Guid UserId,
    string Role,
    ProblemReportKind? Kind,
    string? Text,
    string? Route,
    string? AppVersion,
    ProblemReportDevice Device,
    string? Os,
    string? Browser,
    string? CorrelationId) : ICommand<SubmittedProblemReport>;

public record SubmittedProblemReport(long Id, string Reference);

public class SubmitProblemReportCommandHandler(
    IProblemReportRepository reports,
    IOrgIdentityLinkReader identityLinks)
    : ICommandHandler<SubmitProblemReportCommand, SubmittedProblemReport>
{
    private const int CorrelationIdMaxLength = 64;

    public async Task<SubmittedProblemReport> Handle(SubmitProblemReportCommand cmd, CancellationToken ct)
    {
        if (!await identityLinks.IsLinkedAsync(cmd.UserId, ct))
            throw new ProblemReportNotAllowedException();

        await EnforceLimitsAsync(cmd.UserId, ct);

        var saved = await reports.AddAsync(
            new ProblemReport
            {
                UserId = cmd.UserId,
                Role = cmd.Role,
                Kind = cmd.Kind,
                Text = ProblemReportCleaner.Text(cmd.Text),
                RoutePattern = ProblemReportCleaner.RoutePattern(cmd.Route),
                AppVersion = ProblemReportCleaner.AppVersion(cmd.AppVersion),
                Device = cmd.Device,
                Client = ProblemReportCleaner.Client(cmd.Os, cmd.Browser),
                CorrelationId = ProblemReportCleaner.Token(cmd.CorrelationId, CorrelationIdMaxLength),
            },
            ct);

        return new SubmittedProblemReport(saved.Id, ProblemReportLimits.Reference(saved.Id));
    }

    // The saved rows are the ledger: the hourly and daily caps hold across restarts, unlike the in-memory rate limiter in front.
    private async Task EnforceLimitsAsync(Guid userId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var lastDay = await reports.ListCreatedAtSinceAsync(userId, now.AddDays(-1), ct);

        var lastHour = lastDay.Where(at => at >= now.AddHours(-1)).ToList();
        if (lastHour.Count >= ProblemReportLimits.PerHour)
            throw new ProblemReportRateLimitedException(lastHour[lastHour.Count - ProblemReportLimits.PerHour] + TimeSpan.FromHours(1) - now);

        if (lastDay.Count >= ProblemReportLimits.PerDay)
            throw new ProblemReportRateLimitedException(lastDay[lastDay.Count - ProblemReportLimits.PerDay] + TimeSpan.FromDays(1) - now);
    }
}
