namespace FinanceSentry.Modules.Research.Application.Services.Fundamentals;

/// <summary>
/// Fundamentals provider chain settings (section <c>Fundamentals</c>, #837).
/// </summary>
public sealed class FundamentalsOptions
{
    public const string SectionName = "Fundamentals";

    /// <summary>
    /// Provider order, by <see cref="IFundamentalsSource.Name"/>. Empty means
    /// <see cref="DefaultSourceOrder"/>. A registered source left out of the list is not used; an
    /// unknown name is logged and skipped. (Left null by default: the configuration binder appends to
    /// a pre-filled array instead of replacing it.)
    /// </summary>
    public string[]? SourceOrder { get; set; }

    /// <summary>A quarterly series counts as fresh while its newest period ended within this many days.</summary>
    public int QuarterlyFreshnessDays { get; set; } = 270;

    /// <summary>An annual series counts as fresh while its newest period ended within this many days.</summary>
    public int AnnualFreshnessDays { get; set; } = 548;

    public static readonly IReadOnlyList<string> DefaultSourceOrder =
    [
        FundamentalsSourceNames.SecEdgarUsGaap,
        FundamentalsSourceNames.SecEdgarIfrs,
        FundamentalsSourceNames.YahooFinance,
    ];
}
