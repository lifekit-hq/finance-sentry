namespace FinanceSentry.Modules.Research.Application.Queries;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Repositories;
using Microsoft.Extensions.Options;

/// <param name="Scope">Optional: <c>Book</c>, <c>Sleeve</c> or <c>Thesis</c>.</param>
/// <param name="Window">Optional: <c>1M</c>, <c>3M</c>, <c>1Y</c>, <c>SinceInception</c> (or the enum names).</param>
/// <param name="Ticker">Optional: narrows thesis rows to one ticker; book and sleeve rows are kept.</param>
public record GetBenchmarkTrackRecordQuery(Guid UserId, string? Scope, string? Window, string? Ticker)
    : IQuery<BenchmarkTrackRecordDto>;

/// <summary>
/// Reads the latest stored benchmark-relative run (fs-699). Never recomputes: the figures are
/// materialized by the weekly thesis track-record job, so the API and the agent cite one number.
/// </summary>
public class GetBenchmarkTrackRecordQueryHandler(
    IBenchmarkRelativeRecordRepository records,
    IOptions<RelativePerformanceConfig> ruleOptions)
    : IQueryHandler<GetBenchmarkTrackRecordQuery, BenchmarkTrackRecordDto>
{
    private const string Cadence = "weekly";

    private static readonly Dictionary<string, TrackRecordWindow> WindowAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["1M"] = TrackRecordWindow.OneMonth,
        ["3M"] = TrackRecordWindow.ThreeMonths,
        ["1Y"] = TrackRecordWindow.OneYear,
        ["ITD"] = TrackRecordWindow.SinceInception,
        ["Inception"] = TrackRecordWindow.SinceInception,
    };

    public async Task<BenchmarkTrackRecordDto> Handle(GetBenchmarkTrackRecordQuery query, CancellationToken ct)
    {
        var rule = ruleOptions.Value;
        var ruleDto = new UnderperformanceRuleDto(rule.ThresholdPct, rule.SustainedRuns, rule.Window.ToString(), Cadence);

        if (!TryParseScope(query.Scope, out var scope))
        {
            return Empty(ruleDto, $"Unknown scope '{query.Scope}'. Use Book, Sleeve or Thesis.");
        }

        if (!TryParseWindow(query.Window, out var window))
        {
            return Empty(ruleDto, $"Unknown window '{query.Window}'. Use 1M, 3M, 1Y or SinceInception.");
        }

        var run = await records.ListLatestRunAsync(query.UserId, ct);
        if (run.Count == 0)
        {
            return Empty(ruleDto, "Not materialized yet — the weekly thesis track-record job fills it on its next run.");
        }

        var ticker = string.IsNullOrWhiteSpace(query.Ticker) ? null : query.Ticker.Trim();
        var rows = run
            .Where(r => scope is null || r.Scope == scope)
            .Where(r => window is null || r.Window == window)
            .Where(r => ticker is null || r.Scope != TrackRecordScope.Thesis
                || string.Equals(r.Label, ticker, StringComparison.OrdinalIgnoreCase))
            .OrderBy(r => r.Scope)
            .ThenBy(r => r.Scope == TrackRecordScope.Thesis ? r.Label : r.ScopeKey, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Window)
            .Select(ToDto)
            .ToList();

        var latest = run[0];
        return new BenchmarkTrackRecordDto(
            latest.AsOf,
            run.Max(r => r.ComputedAt),
            latest.BenchmarkTicker,
            ruleDto,
            rows,
            rows.Count == 0 ? "No stored rows match the filter." : null);
    }

    private static BenchmarkTrackRecordDto Empty(UnderperformanceRuleDto rule, string note)
        => new(null, null, ThesisEventRecorder.DefaultBenchmarkTicker, rule, [], note);

    private static bool TryParseScope(string? value, out TrackRecordScope? scope)
    {
        scope = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (Enum.TryParse<TrackRecordScope>(value.Trim(), ignoreCase: true, out var parsed) && Enum.IsDefined(parsed))
        {
            scope = parsed;
            return true;
        }

        return false;
    }

    private static bool TryParseWindow(string? value, out TrackRecordWindow? window)
    {
        window = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        var trimmed = value.Trim();
        if (WindowAliases.TryGetValue(trimmed, out var alias))
        {
            window = alias;
            return true;
        }

        if (Enum.TryParse<TrackRecordWindow>(trimmed, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed))
        {
            window = parsed;
            return true;
        }

        return false;
    }

    private static BenchmarkRelativeRowDto ToDto(BenchmarkRelativeRecord r) => new(
        r.Scope.ToString(),
        r.ScopeKey,
        r.Label,
        r.ThesisId,
        r.Window.ToString(),
        r.Covered,
        r.ConstituentCount,
        r.FromTimestamp,
        r.ToTimestamp,
        r.SubjectReturnPct,
        r.BenchmarkReturnPct,
        r.ExcessReturnPct,
        r.NetExcessReturnPct,
        r.NetGate,
        r.UnderperformingRuns,
        r.SustainedUnderperformance);
}
