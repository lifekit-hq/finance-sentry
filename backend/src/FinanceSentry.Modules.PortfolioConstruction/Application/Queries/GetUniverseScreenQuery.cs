namespace FinanceSentry.Modules.PortfolioConstruction.Application.Queries;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.PortfolioConstruction.API.Responses;
using FinanceSentry.Modules.PortfolioConstruction.Application.Services;
using FinanceSentry.Modules.PortfolioConstruction.Domain;
using FinanceSentry.Modules.PortfolioConstruction.Domain.Ports;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

/// <summary>
/// The ranked universe screen with the archetype's suggested weights (spec 726 US1, US3). The universe is the
/// viewer's held and watchlist names plus the configured curated list, bounded (OQ-1, FR-003); each name is scored
/// by the unchanged 019 scorers behind <see cref="IUniverseScorecardSource"/>; the constructor then gates, orders
/// and weights them under the stricter of the archetype's defaults and the viewer's own limits. Nothing is stored,
/// and nothing here places or prepares a trade (FR-018).
/// </summary>
public sealed record GetUniverseScreenQuery(Guid UserId, string? Archetype) : IQuery<UniverseScreenResult>;

public sealed class GetUniverseScreenQueryHandler(
    IUniverseScorecardSource scorecards,
    IConstructionPolicySource policySource,
    IHeldEquitySource held,
    IWatchlistReader watchlist,
    IMemoryCache cache,
    IOptions<PortfolioConstructionOptions> options,
    TimeProvider clock) : IQueryHandler<GetUniverseScreenQuery, UniverseScreenResult>
{
    public const string HeldSource = "held";
    public const string WatchlistSource = "watchlist";
    public const string CuratedSource = "curated";

    private const string CacheKeyPrefix = "portfolio-construction:scorecard:";

    public async Task<UniverseScreenResult> Handle(GetUniverseScreenQuery query, CancellationToken ct)
    {
        var config = options.Value;
        var (key, archetype) = Resolve(config, query.Archetype);

        var members = await ComposeUniverseAsync(query.UserId, config, ct);
        var cards = await ScorecardsAsync(members.Symbols, config, ct);
        var policy = await policySource.GetAsync(query.UserId, ct);

        var outcome = PortfolioConstructor.Construct(cards, key, archetype, policy);

        var rows = cards
            .Select(card => ToRow(card, members.Sources[card.Symbol], outcome))
            .OrderBy(r => r.ConstructionRank ?? int.MaxValue)
            .ThenBy(r => r.Symbol, StringComparer.Ordinal)
            .ToList();

        return new UniverseScreenResult(
            key,
            [.. config.Archetypes
                .OrderBy(a => a.Key, StringComparer.Ordinal)
                .Select(a => new ArchetypeOptionDto(
                    a.Key.ToLowerInvariant(), a.Value.Label, a.Value.Description, a.Value.EligibleVerdicts,
                    a.Value.MinStructure, a.Value.MinFundamentals, a.Value.ExcludeExtended, a.Value.Ordering))],
            clock.GetUtcNow(),
            config.MaxUniverseSize,
            members.Truncated,
            new ConstructionDto(
                [.. outcome.Constraints.Select(c => new AppliedConstraintDto(c.Key, c.Label, c.Value, c.Source))],
                [.. outcome.Sleeves.Select(s => new SleeveDto(s.AssetClass, s.WeightPct, s.BuiltFromUniverse, s.Note))],
                outcome.CashPct,
                outcome.Notes,
                outcome.Refusal,
                outcome.Entries.Values.Count(e => e.WeightPct > 0m)),
            rows);
    }

    /// <summary>
    /// One scorecard per symbol, in the order asked. A scorecard is shared corpus (nothing in it is per-user), so a
    /// symbol's card is reused across readers for <see cref="PortfolioConstructionOptions.ScorecardCacheMinutes"/>;
    /// a card that could not be read is never cached, so the next read tries again.
    /// </summary>
    private async Task<IReadOnlyList<UniverseScorecard>> ScorecardsAsync(
        IReadOnlyList<string> symbols, PortfolioConstructionOptions config, CancellationToken ct)
    {
        var ttl = TimeSpan.FromMinutes(Math.Max(1, config.ScorecardCacheMinutes));
        var known = new Dictionary<string, UniverseScorecard>(StringComparer.OrdinalIgnoreCase);
        var missing = new List<string>();
        foreach (var symbol in symbols)
        {
            if (cache.TryGetValue(CacheKeyPrefix + symbol, out UniverseScorecard? cached) && cached is not null)
            {
                known[symbol] = cached;
            }
            else
            {
                missing.Add(symbol);
            }
        }

        if (missing.Count > 0)
        {
            foreach (var card in await scorecards.GetAsync(missing, ct))
            {
                known[card.Symbol] = card;
                if (card.Scored)
                {
                    cache.Set(CacheKeyPrefix + card.Symbol, card, ttl);
                }
            }
        }

        return [.. symbols.Select(s => known[s])];
    }

    /// <summary>The archetype named in the request, else the configured default; null when the name is unknown.</summary>
    public static bool TryResolve(PortfolioConstructionOptions config, string? requested, out string key)
    {
        key = string.IsNullOrWhiteSpace(requested) ? config.DefaultArchetype : requested.Trim();
        key = key.ToLowerInvariant();
        return config.Archetypes.ContainsKey(key);
    }

    private static (string Key, Archetype Archetype) Resolve(PortfolioConstructionOptions config, string? requested)
    {
        if (!TryResolve(config, requested, out var key))
        {
            throw new ArgumentException($"Unknown archetype '{requested}'.", nameof(requested));
        }

        return (key, config.Archetypes[key]);
    }

    private async Task<Universe> ComposeUniverseAsync(Guid userId, PortfolioConstructionOptions config, CancellationToken ct)
    {
        var sources = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();

        void Add(IEnumerable<string> symbols, string source)
        {
            foreach (var raw in symbols)
            {
                var symbol = raw.Trim().ToUpperInvariant();
                if (symbol.Length == 0)
                {
                    continue;
                }

                if (!sources.TryGetValue(symbol, out var list))
                {
                    sources[symbol] = list = [];
                    order.Add(symbol);
                }

                if (!list.Contains(source))
                {
                    list.Add(source);
                }
            }
        }

        // The viewer's own names come first, so the cap never drops a name they hold or watch for a curated one.
        Add(await held.ListSymbolsAsync(userId, ct), HeldSource);
        Add(await watchlist.ListTickersAsync(userId, ct), WatchlistSource);
        Add(config.CuratedUniverse, CuratedSource);

        var truncated = order.Count > config.MaxUniverseSize;
        var kept = order.Take(config.MaxUniverseSize).ToList();
        return new Universe(kept, kept.ToDictionary(s => s, s => (IReadOnlyList<string>)sources[s], StringComparer.OrdinalIgnoreCase), truncated);
    }

    private static UniverseRowDto ToRow(
        UniverseScorecard card, IReadOnlyList<string> sources, ConstructionOutcome outcome)
    {
        var entry = outcome.Entries.GetValueOrDefault(card.Symbol);
        var reason = entry?.Reason ?? string.Empty;

        if (!card.Scored)
        {
            return new UniverseRowDto(
                card.Symbol, sources, null, null, card.FailureReason, null, null, null, null, null, null, null, null, reason);
        }

        return new UniverseRowDto(
            card.Symbol,
            sources,
            card.Sector,
            card.Industry,
            null,
            card.ScoredAt,
            new RowVerdictDto(card.VerdictWord ?? "NotScored", card.VerdictRulesVersion, card.FiredRuleId, card.FiredRuleDescription),
            new RowStructureDto(card.StructureRaw, card.StructureAdjusted, card.StructureBasis, card.StructureNotEvaluable),
            new RowFundamentalsDto(card.Fundamentals, card.FundamentalsNotEvaluable),
            new RowCrowdingDto(card.Crowding, card.ExtensionFromMa50),
            new RowValuationDto(
                card.ValuationNotApplicable, card.TrailingPe, card.TrailingPeFiveYearAvg, card.ValuationPremiumPct,
                card.ImpliedUpsidePct, card.ValuationNotEvaluable, card.ValuationStale),
            entry?.Rank,
            entry?.WeightPct,
            reason);
    }

    private sealed record Universe(
        IReadOnlyList<string> Symbols, IReadOnlyDictionary<string, IReadOnlyList<string>> Sources, bool Truncated);
}
