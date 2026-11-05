namespace FinanceSentry.Modules.PortfolioConstruction.Domain;

/// <summary>
/// One portfolio archetype (spec 726 FR-012): an eligibility gate per axis, an ordering and default
/// constraints. Bound from configuration (<c>PortfolioConstruction:Archetypes:{key}</c>), so a new archetype is a
/// configuration change, not code. The defaults are illustrative until calibrated: they follow common retail
/// conventions (a handful of concentrated names for growth, a wider and smaller-position book for balanced) and
/// are always labelled "archetype default" next to the user's own limits, never presented as advice.
/// </summary>
public sealed class Archetype
{
    /// <summary>Shown to the user; the key is the dictionary key it is bound under.</summary>
    public string Label { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Verdict words (by enum name, e.g. <c>StrongBuy</c>, <c>Buy</c>) a name may carry to be built into the
    /// allocation. A name outside this list is listed on the screen but gets no weight.
    /// </summary>
    public List<string> EligibleVerdicts { get; set; } = ["StrongBuy", "Buy"];

    /// <summary>Gate: adjusted structure score at least this. Null = no gate on the axis.</summary>
    public int? MinStructure { get; set; }

    /// <summary>Gate: fundamentals score at least this. A missing score fails the gate (never read as a pass).</summary>
    public int? MinFundamentals { get; set; }

    /// <summary>Gate: crowding must not be <c>Extended</c>. A missing crowding axis does not pass this gate.</summary>
    public bool ExcludeExtended { get; set; } = true;

    /// <summary>
    /// The axes eligible names are ordered by, most important first: <c>structure</c>, <c>fundamentals</c>. Each
    /// key sorts descending and ties fall to the next key, then to the ticker (lexicographic, never a weighted
    /// sum, spec 726 OQ-5).
    /// </summary>
    public List<string> Ordering { get; set; } = ["structure", "fundamentals"];

    /// <summary>The most names the equity sleeve holds.</summary>
    public int NameCount { get; set; } = 10;

    /// <summary>The most one name may weigh, in percent of the whole book.</summary>
    public decimal MaxPositionPct { get; set; } = 10m;

    /// <summary>Default sleeves in percent of the whole book (asset class to target); used when the user has no IPS.</summary>
    public Dictionary<string, decimal> Sleeves { get; set; } = new() { ["Equities"] = 90m, ["Cash"] = 10m };

    /// <summary>The least cash the book holds, in percent. The stricter of this and the user's risk rules applies.</summary>
    public decimal MinCashPct { get; set; } = 5m;
}
