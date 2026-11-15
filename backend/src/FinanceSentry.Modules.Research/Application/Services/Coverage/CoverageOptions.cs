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

    /// <summary>
    /// Coverage names the news ingestion reads beyond its own <c>MaxTickersPerRun</c> budget of held, thesis and watchlist
    /// names, per run. A coverage list longer than this is read in rotating slices
    /// (<see cref="NewsRotationMinutes"/> apart), so the book keeps its place and every covered name is still read.
    /// </summary>
    public int NewsCoverageTickersPerRun { get; set; } = 40;

    /// <summary>Minutes between news ingestion runs, the period the coverage rotation advances by.</summary>
    public int NewsRotationMinutes { get; set; } = 30;

    /// <summary>Days of coverage events kept for the disposition digest; older rows are pruned by the evaluation run.</summary>
    public int EventRetentionDays { get; set; } = 120;

    /// <summary>Most coverage events one digest read returns (newest first); the total that match is always reported.</summary>
    public int MaxDigestEvents { get; set; } = 200;

    /// <summary>
    /// Held names the book is meant to stay within (owner call D2 = 12). A soft cap: it never blocks a buy or removes a
    /// name; <c>list_coverage</c> reports the count against it and the evaluation logs a warning when it is passed.
    /// </summary>
    public int HeldSoftCap { get; set; } = 12;

    /// <summary>
    /// Whether the research memo on coverage entrants (stage 6, the funnel's only model call) runs. Defaults to
    /// <see cref="CoverageMemoMode.LogOnly"/> (owner call D3): the daily cap and the weekly budget bound the spend whatever the
    /// mode; <see cref="CoverageMemoMode.Off"/> makes no call.
    /// </summary>
    public CoverageMemoMode MemoMode { get; set; } = CoverageMemoMode.LogOnly;

    /// <summary>Most memos written in one UTC day across the run; entrants over it wait for a later night (still inside <see cref="MemoLookbackDays"/>).</summary>
    public int MaxMemosPerDay { get; set; } = 3;

    /// <summary>
    /// Most the memo calls may cost in any rolling seven days, at the list prices below (owner call D3 = about $25 a
    /// week). A call is made only when the week's recorded cost plus the call's worst-case cost fits under it.
    /// </summary>
    public decimal MemoWeeklyBudgetUsd { get; set; } = 25m;

    /// <summary>USD per million input tokens used to price a memo call (Sonnet list price).</summary>
    public decimal MemoInputUsdPerMillionTokens { get; set; } = 2m;

    /// <summary>USD per million output tokens used to price a memo call (Sonnet list price).</summary>
    public decimal MemoOutputUsdPerMillionTokens { get; set; } = 10m;

    /// <summary>Output tokens one memo may use; also the output side of the worst-case cost check.</summary>
    public int MemoMaxOutputTokens { get; set; } = 2000;

    /// <summary>Characters of evidence one memo prompt may carry; the rest is cut, never sent. Also bounds the input side of the worst-case cost check.</summary>
    public int MemoMaxInputChars { get; set; } = 40_000;

    /// <summary>Model for the memo; null uses the Agent module's configured model.</summary>
    public string? MemoModelId { get; set; }

    /// <summary>Days back an uncovered-by-memo entrant stays eligible, so a name the daily cap or a failed call passed over is retried.</summary>
    public int MemoLookbackDays { get; set; } = 7;

    /// <summary>Hour (UTC) the daily memo run starts, after the coverage evaluation whose adds it writes up.</summary>
    public int MemoHourUtc { get; set; } = 1;

    /// <summary>Minute of <see cref="MemoHourUtc"/> the memo run starts.</summary>
    public int MemoMinuteUtc { get; set; } = 40;

    /// <summary>Coverage events of the entrant (newest first) the memo prompt carries.</summary>
    public int MemoEventsInPrompt { get; set; } = 10;
}

/// <summary>What the stage 6 memo step does.</summary>
public enum CoverageMemoMode
{
    /// <summary>No call, no spend.</summary>
    Off,

    /// <summary>Entrants get a memo, stored and readable through MCP; nothing acts on it (paper-only). The default.</summary>
    LogOnly,
}
