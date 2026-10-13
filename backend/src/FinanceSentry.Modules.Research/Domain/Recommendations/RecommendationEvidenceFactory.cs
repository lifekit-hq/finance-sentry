namespace FinanceSentry.Modules.Research.Domain.Recommendations;

using System.Globalization;

/// <summary>Evidence lines the handler and the rule sweep write the same way.</summary>
public static class RecommendationEvidenceFactory
{
    /// <summary>The Risk module's single-position rule key (Modules.Risk <c>RiskRuleKeys.MaxPositionWeight</c>).</summary>
    public const string MaxPositionWeightRule = "MaxPositionWeight";

    private const decimal Percent = 100m;

    public static RecommendationEvidence OverCap(decimal weightFraction, decimal capFraction)
        => new(
            RecommendationEvidenceKind.RiskRule,
            string.Create(CultureInfo.InvariantCulture,
                $"{weightFraction * Percent:0.0}% of book against a {capFraction * Percent:0.#}% single-position cap"),
            MaxPositionWeightRule);

    public static RecommendationEvidence NoThesis()
        => new(RecommendationEvidenceKind.Thesis, "No thesis or exit rule on file", null);
}
