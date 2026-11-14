namespace FinanceSentry.Modules.Research.Application.Services.Catalyst;

/// <summary>
/// Catalyst-detection settings bound from configuration (section <c>Catalyst</c>), discovery funnel stage 7: the daily scan of
/// SEC 8-K filings over the reference universe and the GDELT attention measure per theme. Every default is the cautious one: a
/// small per-run nomination cap, request rates well under the providers' ceilings, and an off-switch for each half.
/// </summary>
public sealed class CatalystOptions
{
    public const string SectionName = "Catalyst";

    /// <summary>Master off-switch of the filing scan. False: the job returns at once, makes no request and writes nothing.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// The 8-K items that count as a catalyst, most wanted first (the order ranks nominations when the cap binds): 1.01 a
    /// material definitive agreement, 2.02 results of operations, 5.02 an officer or director change, 8.01 other events.
    /// </summary>
    public List<string> Items { get; set; } = ["1.01", "2.02", "5.02", "8.01"];

    /// <summary>
    /// Which kinds of reference list make up the universe the scan watches (<c>index</c>, <c>theme</c>, <c>sector</c>,
    /// <c>industry</c>). The sector and industry lists hold every US-listed equity, so they are off by default: a filing by a
    /// name in no index and no theme is not a reason to cover it.
    /// </summary>
    public List<string> ListKinds { get; set; } = ["index", "theme"];

    /// <summary>
    /// Days back from today a filing date still counts. A filing is acted on once ever (its accession number is recorded), so a
    /// window longer than the daily cadence only covers a missed run or a weekend; it is not a backfill.
    /// </summary>
    public int LookbackDays { get; set; } = 3;

    /// <summary>
    /// Most names one run nominates into the candidate path for one user. Covered names and names skipped as removed do not count.
    /// A filing beyond the cap is not recorded, so the next run (inside the lookback window) reconsiders it.
    /// </summary>
    public int MaxNominationsPerRun { get; set; } = 3;

    /// <summary>Hour (UTC) the daily scan runs: after the 00:00 opportunity scan and before the 01:10 coverage evaluation that reads its candidates.</summary>
    public int ScanHourUtc { get; set; }

    /// <summary>Minute of <see cref="ScanHourUtc"/> the scan runs.</summary>
    public int ScanMinuteUtc { get; set; } = 30;

    /// <summary>Days of catalyst events kept for the reader; older rows are pruned by the scan.</summary>
    public int EventRetentionDays { get; set; } = 120;

    /// <summary>Most catalyst events one MCP read returns (newest first); the total that match is always reported.</summary>
    public int MaxListedEvents { get; set; } = 200;

    /// <summary>EDGAR full-text search endpoint (undocumented by the SEC: the scan treats any failure or shape change as "use the fallback").</summary>
    public string FullTextUrl { get; set; } = "https://efts.sec.gov/LATEST/search-index";

    /// <summary>Hits asked for per full-text page.</summary>
    public int FullTextPageSize { get; set; } = 100;

    /// <summary>Most full-text pages read per item per run: the bound on one run's request count.</summary>
    public int FullTextMaxPagesPerItem { get; set; } = 8;

    /// <summary>
    /// Requests per second to SEC hosts, full-text search and the fallback's submissions reads together. The SEC's fair-access
    /// ceiling is 10; this stays at half of it.
    /// </summary>
    public int SecRequestsPerSecond { get; set; } = 5;

    /// <summary>
    /// Most tickers the submissions fallback reads in one run (one request each, paced). The universe is read in a stable order
    /// from a rotating start, so a fallback that lasts several days still reads every name.
    /// </summary>
    public int FallbackMaxTickers { get; set; } = 700;

    /// <summary>Master off-switch of the GDELT attention measure.</summary>
    public bool AttentionEnabled { get; set; } = true;

    /// <summary>GDELT DOC 2.0 endpoint.</summary>
    public string GdeltUrl { get; set; } = "https://api.gdeltproject.org/api/v2/doc/doc";

    /// <summary>Days of article volume fetched per phrase per refresh.</summary>
    public int GdeltTimespanDays { get; set; } = 14;

    /// <summary>
    /// Seconds between GDELT requests. GDELT answers HTTP 429 above one request per five seconds, so 5 is the floor, not a tuning knob.
    /// </summary>
    public int GdeltMinIntervalSeconds { get; set; } = 5;

    /// <summary>Hour (UTC) the daily attention refresh runs, before the catalyst scan so a nominee's scorecard carries a fresh reading.</summary>
    public int AttentionHourUtc { get; set; }

    /// <summary>Minute of <see cref="AttentionHourUtc"/> the attention refresh runs.</summary>
    public int AttentionMinuteUtc { get; set; } = 10;

    /// <summary>Days of attention readings kept; older rows are pruned by the refresh.</summary>
    public int AttentionRetentionDays { get; set; } = 90;

    /// <summary>Complete days before the newest that the attention baseline averages over.</summary>
    public int AttentionBaselineDays { get; set; } = 13;

    /// <summary>Fewest baseline days a ratio needs; below it the baseline is withheld rather than guessed.</summary>
    public int AttentionMinBaselineDays { get; set; } = 5;

    /// <summary>
    /// The phrases GDELT is asked about per theme, keyed by the theme id of <c>ReferenceUniverse:Themes</c> (<c>memory</c>, not
    /// <c>theme:memory</c>). The themes themselves are the reference universe's; only the words that mean them in the press live
    /// here. A theme with no phrase is not measured and the refresh says so.
    /// </summary>
    public Dictionary<string, List<string>> ThemePhrases { get; set; } = [];
}
