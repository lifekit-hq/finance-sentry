namespace FinanceSentry.Modules.Research.Domain;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Field-by-field difference between two versions of the policy statement (#700). Pure: it compares
/// the statement's own fields and nothing else, so what the owner changed between two versions is
/// read off the versions, not reconstructed from prose. Metadata that differs by construction
/// (id, version, currency flag, timestamps) is not a change to the policy and is left out.
/// </summary>
public static class IpsDiff
{
    // A decimal keeps the scale it was written with (25 vs 25.00 after a numeric(6,2) round trip), which
    // is not a change to the policy - so figures are compared in their normalised form.
    private static readonly JsonSerializerOptions Options = new() { Converters = { new NormalisedDecimalConverter() } };

    public static IReadOnlyList<IpsFieldChange> Compare(InvestmentPolicyStatement from, InvestmentPolicyStatement to)
    {
        var changes = new List<IpsFieldChange>();

        Add(changes, "goals", from.Goals, to.Goals);
        Add(changes, "primaryHorizonYears", from.PrimaryHorizonYears, to.PrimaryHorizonYears);
        Add(changes, "emergencyCushionUsd", from.EmergencyCushionUsd, to.EmergencyCushionUsd);
        Add(changes, "riskTolerance", from.RiskTolerance, to.RiskTolerance);
        Add(changes, "riskCapacity", from.RiskCapacity, to.RiskCapacity);
        Add(changes, "maxDrawdownTolerancePct", from.MaxDrawdownTolerancePct, to.MaxDrawdownTolerancePct);
        Add(changes, "riskMeasuredAt", from.RiskMeasuredAt, to.RiskMeasuredAt);
        Add(changes, "allocationTargets", from.AllocationTargets, to.AllocationTargets);
        Add(changes, "rebalancingRule", from.RebalancingRule, to.RebalancingRule);
        Add(changes, "contributionPlan", from.ContributionPlan, to.ContributionPlan);
        Add(changes, "sellDiscipline", from.SellDiscipline, to.SellDiscipline);
        Add(changes, "coolingOffDays", from.CoolingOffDays, to.CoolingOffDays);
        Add(changes, "exclusions", from.Exclusions, to.Exclusions);
        Add(changes, "reviewCadence", from.ReviewCadence, to.ReviewCadence);

        return changes;
    }

    private static void Add<T>(List<IpsFieldChange> changes, string field, T from, T to)
    {
        var before = JsonSerializer.Serialize(from, Options);
        var after = JsonSerializer.Serialize(to, Options);
        if (before != after)
            changes.Add(new IpsFieldChange(field, before, after));
    }
}

/// <summary>One field that differs between two statement versions; values are JSON (<c>null</c> = unset).</summary>
public sealed record IpsFieldChange(string Field, string From, string To);

internal sealed class NormalisedDecimalConverter : JsonConverter<decimal>
{
    private const decimal StripTrailingZeros = 1.000000000000000000000000000m;

    public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.GetDecimal();

    public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options)
        => writer.WriteNumberValue(value / StripTrailingZeros);
}
