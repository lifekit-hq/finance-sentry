namespace FinanceSentry.Modules.Research.Application.Services.ReferenceUniverse;

/// <summary>
/// Bound from <c>ReferenceUniverse</c>. The upstream URLs, the FinanceDatabase exchange files, the
/// refresh guard rails and the theme lists (data, not code) all live here.
/// </summary>
public sealed class ReferenceUniverseOptions
{
    public const string SectionName = "ReferenceUniverse";

    /// <summary>S&amp;P 500 constituents (datasets/s-and-p-500-companies, ODC-PDDL-1.0).</summary>
    public string Sp500Url { get; set; } =
        "https://raw.githubusercontent.com/datasets/s-and-p-500-companies/main/data/constituents.csv";

    /// <summary>Current Nasdaq-100 members (unliftedq/index-constitution, MIT).</summary>
    public string Nasdaq100Url { get; set; } =
        "https://raw.githubusercontent.com/unliftedq/index-constitution/main/latest/nasdaq100.csv";

    /// <summary>Directory holding FinanceDatabase's per-exchange equity CSVs (JerBouma/FinanceDatabase, MIT).</summary>
    public string FinanceDatabaseBaseUrl { get; set; } =
        "https://raw.githubusercontent.com/JerBouma/FinanceDatabase/main/database/equities/";

    /// <summary>US-listed exchange files: Nasdaq GS, GM, CM, NYSE and NYSE American.</summary>
    public List<string> FinanceDatabaseExchanges { get; set; } = ["NMS", "NGM", "NCM", "NYQ", "ASE"];

    /// <summary>Per-request timeout; the exchange files are a few MB each.</summary>
    public int RequestTimeoutSeconds { get; set; } = 120;

    /// <summary>A feed returning fewer members than this is treated as broken, not as a shrunken index.</summary>
    public int MinSp500Members { get; set; } = 400;

    public int MinNasdaq100Members { get; set; } = 80;

    public int MinFinanceDatabaseMembers { get; set; } = 2000;

    /// <summary>
    /// A refresh that would drop a source below this fraction of its previous member count is refused
    /// (a truncated download must not wipe a good list). 0.7 tolerates a real index reshuffle.
    /// </summary>
    public double MinRetainedFraction { get; set; } = 0.7;

    /// <summary>A source whose last success is older than this reads as stale (weekly job plus margin).</summary>
    public int StaleAfterDays { get; set; } = 10;

    /// <summary>Theme id to tickers. Declared as data; an edit is a config change, not a code change.</summary>
    public Dictionary<string, List<string>> Themes { get; set; } = [];
}
