namespace FinanceSentry.Modules.Research.Domain.Scoring;

/// <summary>One top holding's weight in the fund (percent) and the word the single-name rules gave it, null when it was not scored.</summary>
public readonly record struct WeightedWord(decimal WeightPct, VerdictWord? Word);

/// <summary>
/// The look-through arithmetic (issue #1324): of the top-10 weight listed, how much sits in holdings reading BUY or
/// better. The denominator is the whole listed weight, so a holding that could not be scored counts against the fund
/// rather than dropping out of the sum: a gap can only lower the share.
/// </summary>
public static class LookThroughWeights
{
    private const decimal PercentScale = 100m;
    private const int Decimals = 1;

    /// <summary>Whether a word counts as "BUY or better".</summary>
    public static bool ReadsBuyOrBetter(VerdictWord? word) => word is VerdictWord.Buy or VerdictWord.StrongBuy;

    /// <summary>Total weight (percent) of the listed holdings.</summary>
    public static decimal Covered(IEnumerable<WeightedWord> holdings) => holdings.Sum(h => h.WeightPct);

    /// <summary>Weight (percent) of the listed holdings reading BUY or better.</summary>
    public static decimal BuyWeight(IEnumerable<WeightedWord> holdings) =>
        holdings.Where(h => ReadsBuyOrBetter(h.Word)).Sum(h => h.WeightPct);

    /// <summary>
    /// The BUY-or-better share of the listed weight in percent (one decimal); null when no holdings are listed, since
    /// there is then nothing to look through to.
    /// </summary>
    public static decimal? BuyWeightSharePct(IReadOnlyCollection<WeightedWord> holdings)
    {
        var covered = Covered(holdings);
        return covered <= 0m
            ? null
            : decimal.Round(BuyWeight(holdings) / covered * PercentScale, Decimals);
    }
}
