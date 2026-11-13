namespace FinanceSentry.Modules.PortfolioConstruction.Application.Services;

using FinanceSentry.Modules.PortfolioConstruction.Domain;
using Microsoft.Extensions.Configuration;

/// <summary>
/// Configuration of the construction screen (section <c>PortfolioConstruction</c>). The archetypes are data, not code
/// (spec 726 FR-012); the universe is not configured here: it is the viewer's held and watchlist names plus the
/// coverage universe the pipeline maintains (<see cref="Core.Interfaces.ICoverageUniverseReader"/>).
/// </summary>
public sealed class PortfolioConstructionOptions
{
    public const string SectionName = "PortfolioConstruction";

    /// <summary>The archetype used when the request names none.</summary>
    public string DefaultArchetype { get; set; } = "growth";

    /// <summary>
    /// Binds the section onto the shipped defaults. A list or dictionary the configuration names replaces the
    /// shipped one (the configuration binder would append to it), so a configured archetype or curated list means
    /// exactly what it says; a key it does not name keeps the shipped value.
    /// </summary>
    public static void Bind(IConfiguration section, PortfolioConstructionOptions options)
    {
        foreach (var configured in section.GetSection(nameof(Archetypes)).GetChildren())
        {
            if (!options.Archetypes.TryGetValue(configured.Key, out var archetype))
            {
                options.Archetypes[configured.Key] = archetype = new Archetype();
            }

            if (configured.GetSection(nameof(Archetype.EligibleVerdicts)).Exists())
            {
                archetype.EligibleVerdicts.Clear();
            }

            if (configured.GetSection(nameof(Archetype.Ordering)).Exists())
            {
                archetype.Ordering.Clear();
            }

            if (configured.GetSection(nameof(Archetype.Sleeves)).Exists())
            {
                archetype.Sleeves.Clear();
            }
        }

        section.Bind(options);
    }

    /// <summary>
    /// The most names one read scores. Every name costs a scorecard read, so the universe is bounded
    /// (spec 726 FR-003); the viewer's own names come first when the cap bites.
    /// </summary>
    public int MaxUniverseSize { get; set; } = 40;

    /// <summary>How long a symbol's scorecard is reused across reads (it is shared, nothing in it is per-user).</summary>
    public int ScorecardCacheMinutes { get; set; } = 30;

    public Dictionary<string, Archetype> Archetypes { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["growth"] = new Archetype
        {
            Label = "Growth",
            Description = "A concentrated book of the strongest-structure names.",
            MinStructure = 60,
            Ordering = ["structure", "fundamentals"],
            NameCount = 10,
            MaxPositionPct = 12m,
            Sleeves = new Dictionary<string, decimal> { ["Equities"] = 90m, ["Cash"] = 10m },
            MinCashPct = 5m,
        },
        ["balanced"] = new Archetype
        {
            Label = "Balanced",
            Description = "A wider book of sound-fundamentals names with smaller positions and a bond sleeve.",
            MinStructure = 50,
            MinFundamentals = 60,
            Ordering = ["fundamentals", "structure"],
            NameCount = 15,
            MaxPositionPct = 6m,
            Sleeves = new Dictionary<string, decimal> { ["Equities"] = 60m, ["Bonds"] = 30m, ["Cash"] = 10m },
            MinCashPct = 5m,
        },
    };
}
