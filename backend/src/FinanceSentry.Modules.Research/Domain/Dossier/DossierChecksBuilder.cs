namespace FinanceSentry.Modules.Research.Domain.Dossier;

/// <summary>
/// One named rule behind an axis of the dossier's summary wheel. <see cref="Passed"/> is null when the rule
/// cannot be evaluated (<see cref="Note"/> says why); otherwise <see cref="Value"/> is the fact the rule read
/// and <see cref="Threshold"/> what it was compared with, both in <see cref="Unit"/>
/// (<c>percent</c>, <c>multiple</c> or <c>none</c>). Never a black-box score: every pass is a fact and a bar.
/// </summary>
public sealed record DossierCheckRule(
    string Key,
    string Label,
    bool? Passed,
    decimal? Value,
    decimal? Threshold,
    string Unit,
    string? Note = null);

/// <summary>An axis of the summary wheel: <see cref="Passed"/> of <see cref="Evaluated"/> rules passed.</summary>
public sealed record DossierCheckAxis(
    string Key,
    string Label,
    int Passed,
    int Evaluated,
    IReadOnlyList<DossierCheckRule> Rules);

/// <summary>
/// The dossier's traceable checks: up to five axes of two rules each. An axis that does not apply to the asset
/// (analysts, growth and value for crypto) is left out rather than scored zero.
/// </summary>
public sealed record DossierChecks(IReadOnlyList<DossierCheckAxis> Axes);

/// <summary>The facts the checks read; every input is optional and a missing one leaves its rule unevaluated.</summary>
public sealed record DossierCheckInputs(
    bool IsCrypto,
    int? BuyRatings,
    int? TotalRatings,
    decimal? ImpliedUpsidePct,
    decimal? RevenueYoyPct,
    decimal? EpsYoyPct,
    decimal? TrailingPe,
    decimal? TrailingPeFiveYearAvg,
    decimal? ForwardPe,
    IReadOnlyList<decimal> PeerForwardPes,
    IReadOnlyList<decimal> YearCloses,
    decimal? YearChangePct,
    decimal? BenchmarkYearChangePct,
    bool IsHeld,
    decimal? ShareOfBookPercent,
    decimal? PositionCapPercent,
    bool HasThesis,
    bool ThesisBroken);

/// <summary>Pure builder for <see cref="DossierChecks"/>; thresholds are the named constants below.</summary>
public static class DossierChecksBuilder
{
    public const string Percent = "percent";
    public const string Multiple = "multiple";
    public const string None = "none";

    public const decimal BuyShareThresholdPct = 50m;
    public const decimal UpsideThresholdPct = 10m;
    public const int MovingAverageDays = 200;

    private const int PercentScale = 100;
    private const int Decimals = 1;

    public static DossierChecks Build(DossierCheckInputs inputs)
    {
        var axes = new List<DossierCheckAxis>();
        if (!inputs.IsCrypto)
        {
            axes.Add(Axis("analysts", "Analysts", AnalystBuyShare(inputs), AnalystUpside(inputs)));
            axes.Add(Axis("growth", "Growth",
                Positive("revenue_growth", "Revenue growing year over year", inputs.RevenueYoyPct, "no comparable revenue filing"),
                Positive("eps_growth", "Earnings per share growing year over year", inputs.EpsYoyPct, "no comparable EPS filing")));
            axes.Add(Axis("value", "Value", PeVsHistory(inputs), ForwardPeVsPeers(inputs)));
        }

        axes.Add(Axis("momentum", "Momentum", AboveMovingAverage(inputs), BeatsBenchmark(inputs)));
        axes.Add(Axis("fit", "Fit", WithinCap(inputs), Thesis(inputs)));
        return new DossierChecks(axes);
    }

    private static DossierCheckAxis Axis(string key, string label, params DossierCheckRule[] rules)
        => new(key, label, rules.Count(r => r.Passed == true), rules.Count(r => r.Passed is not null), rules);

    private static DossierCheckRule AnalystBuyShare(DossierCheckInputs i)
    {
        const string key = "buy_share";
        const string label = "Most analysts rate it Buy";
        if (i.BuyRatings is not int buys || i.TotalRatings is not int total || total <= 0)
        {
            return Unevaluated(key, label, Percent, BuyShareThresholdPct, "no recommendation trend");
        }

        var share = Math.Round((decimal)buys / total * PercentScale, Decimals);
        return new DossierCheckRule(key, label, share >= BuyShareThresholdPct, share, BuyShareThresholdPct, Percent,
            $"{buys} of {total} rate it Buy or Strong Buy");
    }

    private static DossierCheckRule AnalystUpside(DossierCheckInputs i)
    {
        const string key = "target_upside";
        const string label = "Analyst target at least 10% above the price";
        return i.ImpliedUpsidePct is decimal upside
            ? new DossierCheckRule(key, label, upside >= UpsideThresholdPct, upside, UpsideThresholdPct, Percent)
            : Unevaluated(key, label, Percent, UpsideThresholdPct, "no analyst target");
    }

    private static DossierCheckRule Positive(string key, string label, decimal? value, string missing)
        => value is decimal v
            ? new DossierCheckRule(key, label, v > 0m, v, 0m, Percent)
            : Unevaluated(key, label, Percent, 0m, missing);

    private static DossierCheckRule PeVsHistory(DossierCheckInputs i)
    {
        const string key = "pe_vs_history";
        const string label = "P/E below its own 5-year average";
        if (i.TrailingPe is not decimal pe || pe <= 0m)
        {
            return Unevaluated(key, label, Multiple, i.TrailingPeFiveYearAvg, "no positive trailing P/E");
        }

        return i.TrailingPeFiveYearAvg is decimal average && average > 0m
            ? new DossierCheckRule(key, label, pe < average, pe, average, Multiple)
            : Unevaluated(key, label, Multiple, null, "no 5-year P/E history yet") with { Value = pe };
    }

    private static DossierCheckRule ForwardPeVsPeers(DossierCheckInputs i)
    {
        const string key = "forward_pe_vs_peers";
        const string label = "Forward P/E at or below the peer median";
        var peers = i.PeerForwardPes.Where(p => p > 0m).OrderBy(p => p).ToList();
        var median = peers.Count == 0
            ? (decimal?)null
            : peers.Count % 2 == 1
                ? peers[peers.Count / 2]
                : (peers[(peers.Count / 2) - 1] + peers[peers.Count / 2]) / 2m;
        median = median is decimal m ? Math.Round(m, Decimals) : null;

        if (i.ForwardPe is not decimal forward || forward <= 0m)
        {
            return Unevaluated(key, label, Multiple, median, "no positive forward P/E");
        }

        return median is decimal peerMedian
            ? new DossierCheckRule(key, label, forward <= peerMedian, forward, peerMedian, Multiple,
                $"median of {peers.Count} peers")
            : Unevaluated(key, label, Multiple, null, "no peer forward P/E") with { Value = forward };
    }

    private static DossierCheckRule AboveMovingAverage(DossierCheckInputs i)
    {
        const string key = "above_200d";
        const string label = "Price above its 200-day average";
        if (i.YearCloses.Count < MovingAverageDays)
        {
            return Unevaluated(key, label, Percent, 0m, "fewer than 200 daily closes");
        }

        var average = i.YearCloses.TakeLast(MovingAverageDays).Average();
        if (average <= 0m)
        {
            return Unevaluated(key, label, Percent, 0m, "no positive 200-day average");
        }

        var distance = Math.Round((i.YearCloses[^1] / average - 1m) * PercentScale, Decimals);
        return new DossierCheckRule(key, label, distance > 0m, distance, 0m, Percent);
    }

    private static DossierCheckRule BeatsBenchmark(DossierCheckInputs i)
    {
        const string key = "beats_sp500";
        const string label = "Beat the S&P 500 over the past year";
        if (i.YearChangePct is not decimal change)
        {
            return Unevaluated(key, label, Percent, i.BenchmarkYearChangePct, "no one-year price history");
        }

        return i.BenchmarkYearChangePct is decimal benchmark
            ? new DossierCheckRule(key, label, change > benchmark, change, benchmark, Percent)
            : Unevaluated(key, label, Percent, null, "no S&P 500 history") with { Value = change };
    }

    private static DossierCheckRule WithinCap(DossierCheckInputs i)
    {
        const string key = "within_cap";
        const string label = "Position within the IPS size cap";
        if (!i.IsHeld || i.ShareOfBookPercent is not decimal share)
        {
            return Unevaluated(key, label, Percent, i.PositionCapPercent, "not held");
        }

        return i.PositionCapPercent is decimal cap
            ? new DossierCheckRule(key, label, share <= cap, share, cap, Percent)
            : Unevaluated(key, label, Percent, null, "no position cap in the IPS") with { Value = share };
    }

    private static DossierCheckRule Thesis(DossierCheckInputs i)
    {
        const string key = "has_thesis";
        const string label = "Has a written thesis that still holds";
        if (!i.HasThesis)
        {
            return new DossierCheckRule(key, label, false, null, null, None, "no thesis written");
        }

        return new DossierCheckRule(key, label, !i.ThesisBroken, null, null, None,
            i.ThesisBroken ? "thesis marked broken" : null);
    }

    private static DossierCheckRule Unevaluated(string key, string label, string unit, decimal? threshold, string note)
        => new(key, label, null, null, threshold, unit, note);
}
