namespace FinanceSentry.Modules.Risk.Application.Services;

/// <summary>
/// Operational parameters for the Risk module bound from configuration (section <c>Risk</c>).
/// Rule THRESHOLDS (max position weight, cash buffer, etc.) are NOT here — they live in the
/// persisted <see cref="Domain.RiskRuleSet"/> and are the user's decisions, never defaulted.
/// This holds only runtime knobs (windows, schedule) so there are no magic numbers in code.
/// </summary>
public sealed class RiskOptions
{
    public const string SectionName = "Risk";

    /// <summary>Rolling window (days) for turnover counting and add-to-broken-thesis history lookback.</summary>
    public int RollingQuarterDays { get; set; } = 90;

    /// <summary>
    /// Window (days) of holding snapshots the book's drawdown is measured over (#700): the peak is the
    /// highest point inside it, so a decline older than the window no longer counts against the tolerance.
    /// </summary>
    public int DrawdownLookbackDays { get; set; } = 365;

    /// <summary>Hour of day (UTC) the daily <see cref="Infrastructure.Jobs.RiskCheckJob"/> runs (post-sync).</summary>
    public int RiskCheckHourUtc { get; set; } = 7;
}
