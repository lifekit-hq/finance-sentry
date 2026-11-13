namespace FinanceSentry.Modules.Research.Application.Services.Coverage;

/// <summary>
/// Coverage-universe settings bound from configuration (section <c>Coverage</c>): the cap and the thresholds of
/// the add and remove rules. No threshold lives in the rules themselves.
/// </summary>
public sealed class CoverageOptions
{
    public const string SectionName = "Coverage";

    /// <summary>
    /// Most names the coverage universe holds. The cap limits the ranked adds (a scored or nominated candidate);
    /// held and watchlisted names are the owner's own acts and always join, so they can take the universe over
    /// the cap, in which case no ranked add fits until they leave.
    /// </summary>
    public int MaxMembers { get; set; } = 100;

    /// <summary>Days a verdict stays in the window the "Buy twice" add rule reads.</summary>
    public int BuyWindowDays { get; set; } = 14;

    /// <summary>Distinct days a candidate must read Buy or StrongBuy inside the window to be added.</summary>
    public int BuyObservationsRequired { get; set; } = 2;

    /// <summary>Consecutive Avoid verdicts (the latest observations) that remove an unprotected name.</summary>
    public int AvoidObservationsRequired { get; set; } = 2;

    /// <summary>Days without an event, with no dated catalyst ahead, after which an unprotected name is removed.</summary>
    public int NoEventDays { get; set; } = 60;

    /// <summary>
    /// How far ahead a dated catalyst (an earnings date, a thesis catalyst) keeps an otherwise idle name covered.
    /// </summary>
    public int CatalystLookaheadDays { get; set; } = 60;

    /// <summary>
    /// Days of stored verdict readings the rules look back over (the Avoid streak reads these; the Buy rule reads
    /// the shorter <see cref="BuyWindowDays"/>). Keep it at least <see cref="BuyWindowDays"/>.
    /// </summary>
    public int VerdictLookbackDays { get; set; } = 30;

    /// <summary>Days of verdict observations kept; older rows are pruned by the evaluation run.</summary>
    public int ObservationRetentionDays { get; set; } = 120;

    /// <summary>
    /// Most verdict reads (one scorecard each) one evaluation run makes. Candidates already covered are read
    /// first, then the most recently scored; the rest wait for the next night.
    /// </summary>
    public int MaxVerdictReadsPerRun { get; set; } = 100;

    /// <summary>Hour (UTC) the daily evaluation runs, after the 00:00 opportunity scan and candidate expiry.</summary>
    public int EvaluationHourUtc { get; set; } = 1;

    /// <summary>Minute of <see cref="EvaluationHourUtc"/> the evaluation runs.</summary>
    public int EvaluationMinuteUtc { get; set; } = 10;

    /// <summary>Most members the MCP <c>list_coverage</c> and the construction screen read in one call.</summary>
    public int MaxListed { get; set; } = 500;
}
