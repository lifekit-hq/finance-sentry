namespace FinanceSentry.Core.Headlines;

using FinanceSentry.Core.Measures;
using FinanceSentry.Core.Utils;

/// <summary>The quality and reasons a figure's signals add up to.</summary>
public sealed record HeadlineAssessment(HeadlineQuality Quality, IReadOnlyList<HeadlineReason> Reasons);

/// <summary>
/// The single place today's signals become a <see cref="Headline"/>'s quality, basis status and reasons
/// (finance-framework §3.6 mapping table). The quality is the worst any reason implies; a figure with no amount
/// is <see cref="HeadlineQuality.Unavailable"/> whatever its reasons, and never carries an empty reason list
/// (<see cref="HeadlineReasonCode.NotMeasurable"/> stands in when nothing else explains it).
/// </summary>
public static class HeadlineQualityMapper
{
    /// <summary>
    /// Maps <paramref name="signals"/> (and, when the percent was withheld, the measure's
    /// <paramref name="percentFailure"/>) onto a quality and an ordered, de-duplicated reason list.
    /// </summary>
    public static HeadlineAssessment Assess(
        HeadlineSignals signals, bool amountAvailable, MeasureFailure? percentFailure = null)
    {
        var reasons = new List<HeadlineReason>();

        foreach (var source in signals.StaleSources.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
            reasons.Add(new HeadlineReason(HeadlineReasonCode.StaleSource, source));

        // A reconstructed day is valued at today's rate (money-semantics §8), so it carries both reasons.
        var fx = signals.Fx;
        if (signals.BackfilledEstimate)
        {
            reasons.Add(new HeadlineReason(HeadlineReasonCode.BackfilledEstimate));
            fx = fx.Worst(FxStatus.TodayRate);
        }

        if (ReasonFor(fx) is { } fxReason)
            reasons.Add(new HeadlineReason(fxReason));

        if (signals.NoSplit)
            reasons.Add(new HeadlineReason(HeadlineReasonCode.NoSplit));

        if (signals.NoCostBasis)
            reasons.Add(new HeadlineReason(HeadlineReasonCode.NoCostBasis));

        foreach (var source in signals.UnknownFlowSources.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
            reasons.Add(new HeadlineReason(HeadlineReasonCode.UnknownFlow, source));

        if (signals.ScopeChanged)
            reasons.Add(new HeadlineReason(HeadlineReasonCode.ScopeChanged));

        foreach (var source in signals.UnmatchedTransferSources.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
            reasons.Add(new HeadlineReason(HeadlineReasonCode.UnmatchedTransfer, source));

        if (percentFailure is { } failure)
            reasons.Add(new HeadlineReason(ReasonFor(failure)));

        return Build(reasons, amountAvailable);
    }

    /// <summary>
    /// <paramref name="headline"/> with one more conversion applied (the base currency it is shown in): the FX
    /// reason becomes the worse of the one it carried and <paramref name="status"/>.
    /// </summary>
    public static Headline WithFx(Headline headline, FxStatus status)
    {
        var carried = headline.Reasons.Any(r => r.Code == HeadlineReasonCode.FxUnknown1To1) ? FxStatus.Unknown1To1
            : headline.Reasons.Any(r => r.Code == HeadlineReasonCode.FxTodayRate) ? FxStatus.TodayRate
            : FxStatus.Exact;
        var worst = carried.Worst(status);
        if (worst == carried)
            return headline;

        var reasons = headline.Reasons
            .Where(r => r.Code is not (HeadlineReasonCode.FxTodayRate or HeadlineReasonCode.FxUnknown1To1))
            .ToList();
        if (ReasonFor(worst) is { } fxReason)
            reasons.Add(new HeadlineReason(fxReason));

        var assessment = Build(reasons, headline.Amount is not null);
        return headline with { Quality = assessment.Quality, Reasons = assessment.Reasons };
    }

    /// <summary>A position-returns coverage (<c>complete</c> / <c>partial</c> / <c>unavailable</c>) as a basis status.</summary>
    public static BasisStatus BasisStatusFromCoverage(string coverage) => coverage.ToLowerInvariant() switch
    {
        "complete" => BasisStatus.Complete,
        "partial" => BasisStatus.Partial,
        _ => BasisStatus.Missing,
    };

    /// <summary>The basis status of a figure whose basis is known for <paramref name="measured"/> of <paramref name="total"/> items.</summary>
    public static BasisStatus BasisStatusFromCounts(int measured, int total) =>
        measured <= 0 ? BasisStatus.Missing
        : measured >= total ? BasisStatus.Complete
        : BasisStatus.Partial;

    /// <summary>The reason a conversion status carries, or null when the rate was the date's own.</summary>
    public static HeadlineReasonCode? ReasonFor(FxStatus status) => status.ToReason() switch
    {
        FxReasons.TodayRate => HeadlineReasonCode.FxTodayRate,
        FxReasons.Unknown1To1 => HeadlineReasonCode.FxUnknown1To1,
        _ => null,
    };

    /// <summary>The reason a measure's failure gives for a missing percent.</summary>
    public static HeadlineReasonCode ReasonFor(MeasureFailure failure) => failure switch
    {
        MeasureFailure.ZeroBase => HeadlineReasonCode.ZeroBase,
        MeasureFailure.NegativeBase => HeadlineReasonCode.NegativeBase,
        MeasureFailure.ScopeChanged => HeadlineReasonCode.ScopeChanged,
        MeasureFailure.MissingValue or MeasureFailure.InsufficientData => HeadlineReasonCode.ShortHistory,
        _ => HeadlineReasonCode.NotMeasurable,
    };

    /// <summary>
    /// The quality a reason implies on its own. Reasons that only explain a withheld percent leave the amount
    /// complete; the reasons no source raises yet take the quality their meaning implies.
    /// </summary>
    public static HeadlineQuality QualityOf(HeadlineReasonCode code) => code switch
    {
        HeadlineReasonCode.StaleSource => HeadlineQuality.Degraded,
        HeadlineReasonCode.BackfilledEstimate => HeadlineQuality.Estimated,
        HeadlineReasonCode.FxTodayRate => HeadlineQuality.Estimated,
        HeadlineReasonCode.FxUnknown1To1 => HeadlineQuality.Degraded,
        HeadlineReasonCode.NoSplit => HeadlineQuality.Unavailable,
        HeadlineReasonCode.NoCostBasis => HeadlineQuality.Unavailable,
        HeadlineReasonCode.UnknownFlow => HeadlineQuality.Estimated,
        HeadlineReasonCode.UnmatchedTransfer => HeadlineQuality.Estimated,
        HeadlineReasonCode.PriceCarriedForward => HeadlineQuality.Estimated,
        HeadlineReasonCode.NonCashIncluded => HeadlineQuality.Degraded,
        HeadlineReasonCode.ResidualAboveTolerance => HeadlineQuality.Degraded,
        _ => HeadlineQuality.Complete,
    };

    private static HeadlineAssessment Build(List<HeadlineReason> reasons, bool amountAvailable)
    {
        if (!amountAvailable && reasons.Count == 0)
            reasons.Add(new HeadlineReason(HeadlineReasonCode.NotMeasurable));

        var ordered = reasons
            .Distinct()
            .OrderBy(r => r.Code)
            .ThenBy(r => r.Source, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var quality = ordered
            .Select(r => QualityOf(r.Code))
            .DefaultIfEmpty(HeadlineQuality.Complete)
            .Max();
        if (!amountAvailable)
            quality = HeadlineQuality.Unavailable;

        return new HeadlineAssessment(quality, ordered);
    }
}
