namespace FinanceSentry.Modules.Research.Application.Services.Screens;

/// <summary>
/// Settings of the discovery-funnel screens (stage 5), bound from <c>Screens</c>. The screens nominate into the
/// existing candidate path; every bar and cap lives here, none in the rules. Defaults are the safe ones: a per-screen and
/// a per-user cap on nominations a run, the Coverage cap respected, and no model call anywhere.
/// </summary>
public sealed class ScreenOptions
{
    public const string SectionName = "Screens";

    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Hour (UTC) of the nightly run: after Radar's 23:00 compute has refreshed the stored bars the relative-strength
    /// screen reads, and before the 00:00 opportunity scan, so a name both find is attributed to the screen.
    /// </summary>
    public int RunHourUtc { get; set; } = 23;

    public int RunMinuteUtc { get; set; } = 30;

    /// <summary>
    /// The reference lists the screens draw from (<c>sp500</c>, <c>nasdaq100</c>, <c>sector:Energy</c>, <c>theme:memory</c>).
    /// A trailing <c>*</c> takes every list with that prefix (<c>theme:*</c>).
    /// </summary>
    public List<string> ListKeys { get; set; } = ["sp500", "nasdaq100", "theme:*"];

    /// <summary>Most names all screens together nominate for one user in one run; the first screens in order fill it first.</summary>
    public int MaxNominationsPerUserPerRun { get; set; } = 10;

    /// <summary>
    /// Days after a coverage removal before the name can be nominated again, on top of needing evidence later than the
    /// removal (<c>ScreenSuppression</c>).
    /// </summary>
    public int RemovedCooldownDays { get; set; } = 30;

    public FundamentalsScreenOptions Fundamentals { get; set; } = new();

    public RelativeStrengthScreenOptions RelativeStrength { get; set; } = new();

    public CatalystScreenOptions Catalyst { get; set; } = new();
}

/// <summary>Revenue growth, operating cash flow margin, capex intensity and equity of the latest fiscal year (EDGAR facts).</summary>
public sealed class FundamentalsScreenOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Tickers whose EDGAR facts one run reads. The list is read in rotating daily slices of this size, so every name
    /// is read once every <c>count / BatchSize</c> nights; 40 names of about fifteen requests each stay far under
    /// SEC's 10 requests a second.
    /// </summary>
    public int BatchSize { get; set; } = 40;

    /// <summary>Milliseconds between two tickers' reads, on top of the EDGAR service's own concurrency cap and caches.</summary>
    public int PauseBetweenTickersMs { get; set; } = 250;

    /// <summary>Datapoints per concept requested from the chain; three years leaves room for the year-earlier pairing among the quarterly facts.</summary>
    public int FactsPerConcept { get; set; } = 12;

    /// <summary>Revenue growth of the latest fiscal year at or above this (0.15 = 15%).</summary>
    public decimal MinRevenueGrowth { get; set; } = 0.15m;

    /// <summary>Operating cash flow over revenue at or above this.</summary>
    public decimal MinOperatingCashFlowMargin { get; set; } = 0.15m;

    /// <summary>Capex over revenue at or below this: the screen prefers growth that does not need the balance sheet to fund it.</summary>
    public decimal MaxCapexIntensity { get; set; } = 0.15m;

    /// <summary>Stockholders' equity must be strictly above this.</summary>
    public decimal MinStockholdersEquity { get; set; }

    /// <summary>A fiscal year ended longer ago than this is stale evidence, not a pass.</summary>
    public int MaxFactAgeDays { get; set; } = 550;

    /// <summary>Days after a fiscal year's end its annual report is taken to be public; the hit's evidence date is the year end plus this.</summary>
    public int FilingLagDays { get; set; } = 75;

    /// <summary>Most names this screen nominates for one user in one run.</summary>
    public int MaxNominationsPerRun { get; set; } = 3;
}

/// <summary>
/// Relative strength among the reference-list names Radar keeps bars for, ranked by the opportunity scan's own
/// nomination rules (<c>ScanNominationRules</c>) and thresholds (<c>Opportunity</c>), so the rules are not duplicated.
/// </summary>
public sealed class RelativeStrengthScreenOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Fewest reference-list names with usable bars for the percentile ranks to mean anything; below it the screen
    /// reports "pool too small" and nominates nothing, rather than calling the best of three a top decile.
    /// </summary>
    public int MinPoolSize { get; set; } = 20;

    public int MaxNominationsPerRun { get; set; } = 3;
}

/// <summary>Reference-list names reporting earnings within the window, from Finnhub's earnings calendar (one request a run).</summary>
public sealed class CatalystScreenOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>Earnings dates within this many days ahead (inclusive of today) count as a dated catalyst.</summary>
    public int LookaheadDays { get; set; } = 10;

    public int MaxNominationsPerRun { get; set; } = 4;
}
