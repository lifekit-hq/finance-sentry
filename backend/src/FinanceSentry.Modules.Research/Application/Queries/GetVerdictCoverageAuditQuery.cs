namespace FinanceSentry.Modules.Research.Application.Queries;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Application.Services.Etf;
using FinanceSentry.Modules.Research.Application.Services.Fundamentals;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Fundamentals;
using FinanceSentry.Modules.Research.Domain.Scoring;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// The verdict-coverage audit (#1413, plan #1273 section 2): for every held, thesis, watched and coverage symbol of the
/// owner, the state of each axis and section a verdict reads and a <see cref="VerdictCoverageGap"/> where one has no
/// usable data. It reads through the same readers the scorecard and the dossier use (structure, fundamentals, valuation,
/// peers, <see cref="ScorecardReader"/> and its ETF look-through), so a row says what the verdict itself saw. Read-only:
/// nothing is stored, nothing leaves the app, and no word it reads is acted on. The owner's holdings are read at request
/// time through <see cref="IBookFiguresService"/> and <see cref="IBrokerageHoldingsReader"/>, so the caller is the owner
/// (the endpoint and the MCP tool need <c>ai.use</c>). Input is capped at <see cref="MaxSymbols"/>, held names first.
/// </summary>
public sealed record GetVerdictCoverageAuditQuery(Guid UserId) : IQuery<VerdictCoverageAudit>
{
    public const int MaxSymbols = 100;
}

public sealed class GetVerdictCoverageAuditQueryHandler(
    IBookFiguresService bookFigures,
    IBrokerageHoldingsReader brokerageHoldings,
    IWatchlistReader watchlist,
    IThesisTickerReader thesisTickers,
    ICoverageUniverseReader coverage,
    IMarketStructureReader structureReader,
    IFundamentalsService fundamentals,
    ScorecardValuationReader valuationReader,
    ScorecardReader scorecards,
    IFundHoldingsSource fundHoldings,
    IQueryHandler<GetAssetDossierQuery, AssetDossierResult> dossier,
    IOptions<VerdictOptions> verdictOptions,
    ILogger<GetVerdictCoverageAuditQueryHandler> logger)
    : IQueryHandler<GetVerdictCoverageAuditQuery, VerdictCoverageAudit>
{
    private const string EquityInstrumentType = "STK";

    public async Task<VerdictCoverageAudit> Handle(GetVerdictCoverageAuditQuery query, CancellationToken ct)
    {
        var userId = query.UserId;

        // The universe reads are not wrapped: a failure there must fail the audit, not silently drop symbols from it.
        var book = await bookFigures.ReadAsync(userId, ct);
        var brokerageRows = await brokerageHoldings.GetHoldingsAsync(userId, ct);
        var candidates = new Candidates();
        candidates.Add(book.Positions.Select(p => p.Symbol), VerdictCoverageOrigins.Held);
        candidates.Add(await thesisTickers.ListTickersAsync(userId, ct), VerdictCoverageOrigins.Thesis);
        candidates.Add(await watchlist.ListTickersAsync(userId, ct), VerdictCoverageOrigins.Watched);
        candidates.Add(await coverage.ListTickersAsync(userId, ct), VerdictCoverageOrigins.Coverage);

        // One after another: the structure, fundamentals and valuation reads share this request's scoped DbContext.
        var rows = new List<VerdictCoverageRow>();
        foreach (var candidate in candidates.Ordered.Take(GetVerdictCoverageAuditQuery.MaxSymbols))
        {
            var position = book.Positions.FirstOrDefault(p => p.Symbol.Equals(candidate.Symbol, StringComparison.OrdinalIgnoreCase));
            var brokerageRow = brokerageRows.FirstOrDefault(h => h.Symbol.Equals(candidate.Symbol, StringComparison.OrdinalIgnoreCase));
            rows.Add(await AuditAsync(userId, candidate, position, brokerageRow, ct));
        }

        return new VerdictCoverageAudit(DateTimeOffset.UtcNow, candidates.Ordered.Count, candidates.Ordered.Count > rows.Count, rows);
    }

    private async Task<VerdictCoverageRow> AuditAsync(
        Guid userId, Candidate candidate, BookFigurePosition? position, BrokerageHoldingSummary? brokerageRow, CancellationToken ct)
    {
        var symbol = candidate.Symbol;
        var isCrypto = DossierTickers.IsCrypto(position?.AssetClass);
        var instrumentType = brokerageRow?.InstrumentType;

        var bookFacts = new CoverageBookFacts(
            brokerageRow?.Provider ?? position?.Provider,
            instrumentType,
            position?.AssetClass,
            brokerageRow is null ? VerdictCoverageGap.SourceMissing : null);

        // The Radar universe takes a held name only when it is an equity; a name on a watchlist, thesis or coverage list is in.
        var onlyHeld = candidate.Origins.Count == 1 && candidate.Origins[0] == VerdictCoverageOrigins.Held;
        var universe = onlyHeld && !string.Equals(instrumentType, EquityInstrumentType, StringComparison.OrdinalIgnoreCase)
            ? new CoverageSection(VerdictCoverageGap.OutsideUniverse, instrumentType ?? position?.AssetClass)
            : new CoverageSection(null);

        var (structure, bars) = await ReadStructureAsync(symbol, ct);
        var structureMissing = structure.Gap == VerdictCoverageGap.SymbolMapping;

        var (axis, sector, industry, peerPes, valuation, peers) = await ReadValuationAsync(symbol, isCrypto, structureMissing, ct);
        var fundamentalsSection = await ReadFundamentalsAsync(symbol, isCrypto, axis, ct);
        var facts = fundamentalsSection.Facts;

        var verdict = await ReadVerdictAsync(symbol, axis, facts, sector, industry, peerPes, isCrypto, ct);

        var fund = await ReadFundAsync(symbol, isCrypto, axis, ct);
        var dossierSection = await ReadDossierAsync(symbol, userId, ct);

        return new VerdictCoverageRow(
            symbol,
            candidate.Origins,
            bookFacts,
            universe,
            structure,
            fundamentalsSection.Section,
            valuation,
            peers,
            LookThroughSection(isCrypto, axis, verdict.Card, verdict.Verdict),
            verdict.Verdict,
            bars,
            fund,
            dossierSection);
    }

    private async Task<(CoverageSection Section, CoverageBars? Bars)> ReadStructureAsync(string symbol, CancellationToken ct)
    {
        var read = await TryAsync(symbol, "structure", () => structureReader.GetStructureAsync(symbol, ct), ct);
        if (read.Failure is not null)
        {
            return (Threw(read.Failure), null);
        }

        var bars = read.Value is { } found ? new CoverageBars(found.BarCount, found.LastBarDate) : null;
        var section = read.Value switch
        {
            null => new CoverageSection(VerdictCoverageGap.SymbolMapping, "no bars for the bare symbol"),
            { Stale: true } => new CoverageSection(VerdictCoverageGap.FeedLimit, "stale bars"),
            var snapshot when snapshot.RsByWindow.Values.All(v => v is null) =>
                new CoverageSection(VerdictCoverageGap.FeedLimit, "no relative-strength window has enough history"),
            { ExtensionFromMa50: null } => new CoverageSection(VerdictCoverageGap.FeedLimit, "no extension from the 50-day average"),
            _ => new CoverageSection(null),
        };
        return (section, bars);
    }

    /// <summary>
    /// The fund kind the holdings source reports, read only where the scorecard itself asks (a non-crypto name whose
    /// valuation does not apply); null when it was not read. A source that fails reads as <c>Unknown</c>, which is what
    /// the scorecard treats it as.
    /// </summary>
    private async Task<CoverageFund?> ReadFundAsync(string symbol, bool isCrypto, ScorecardValuationAxis axis, CancellationToken ct)
    {
        if (isCrypto || !axis.NotApplicable)
        {
            return null;
        }

        var read = await TryAsync(symbol, "fund holdings", () => fundHoldings.GetTopHoldingsAsync(symbol, ct), ct);
        return read.Failure is not null
            ? new CoverageFund(nameof(FundKind.Unknown), null, 0)
            : new CoverageFund(read.Value.Kind.ToString(), read.Value.Source, read.Value.Holdings.Count);
    }

    /// <summary>
    /// Which sections of the symbol's dossier came back empty because their read threw (the dossier hides that behind a
    /// null section). The scorecard is left to the verdict row, so it is not read twice.
    /// </summary>
    private async Task<CoverageSection> ReadDossierAsync(string symbol, Guid userId, CancellationToken ct)
    {
        var read = await TryAsync(
            symbol, "dossier", () => dossier.Handle(new GetAssetDossierQuery(userId, symbol, IncludeVerdicts: false), ct), ct);
        if (read.Failure is not null)
        {
            return Threw(read.Failure);
        }

        var failed = read.Value?.FailedSections ?? [];
        return failed.Count == 0
            ? new CoverageSection(null)
            : new CoverageSection(VerdictCoverageGap.SectionThrew, string.Join(", ", failed));
    }

    private async Task<(
        ScorecardValuationAxis Axis,
        string? Sector,
        string? Industry,
        IReadOnlyList<decimal?>? PeerPes,
        CoverageSection Valuation,
        CoverageSection Peers)> ReadValuationAsync(string symbol, bool isCrypto, bool structureMissing, CancellationToken ct)
    {
        if (isCrypto)
        {
            var crypto = new CoverageSection(VerdictCoverageGap.NotApplicable, "crypto");
            return (ScorecardValuationAxis.NotApplicableAxis, null, null, null, crypto, crypto);
        }

        var read = await TryAsync(symbol, "valuation", () => valuationReader.ReadAsync(symbol, ct), ct);
        if (read.Failure is not null)
        {
            var threw = Threw(read.Failure);
            return (ScorecardValuationAxis.Unavailable, null, null, null, threw, threw);
        }

        var (axis, sector, industry, peerPes) = read.Value;
        if (axis.NotApplicable)
        {
            var notApplicable = new CoverageSection(VerdictCoverageGap.NotApplicable, "valuation not applicable to this name");
            return (axis, sector, industry, peerPes, notApplicable, notApplicable);
        }

        if (axis.NotEvaluableReason == ScorecardValuationAxis.ReasonUnavailable)
        {
            var unavailable = new CoverageSection(
                structureMissing ? VerdictCoverageGap.SymbolMapping : VerdictCoverageGap.FeedLimit, "no current valuation metrics");
            return (axis, sector, industry, peerPes, unavailable, PeersSection(axis, peerPes));
        }

        var lens = axis.WithPeerLens(peerPes ?? [], verdictOptions.Value.MinPeersForPriceLens).PriceLens;
        var valuation = lens is null
            ? new CoverageSection(VerdictCoverageGap.FeedLimit, $"no price lens ({axis.NotEvaluableReason ?? "no yardstick"})")
            : new CoverageSection(null, $"lens={lens}");
        return (axis, sector, industry, peerPes, valuation, PeersSection(axis, peerPes));
    }

    private CoverageSection PeersSection(ScorecardValuationAxis axis, IReadOnlyList<decimal?>? peerPes)
    {
        if (axis.PremiumPct is not null)
        {
            return new CoverageSection(null, "not needed: own-history lens");
        }

        if (axis.ForwardPe is not > 0m)
        {
            return new CoverageSection(VerdictCoverageGap.NotApplicable, "no positive forward P/E to compare");
        }

        var minPeers = verdictOptions.Value.MinPeersForPriceLens;
        var positive = peerPes?.Count(pe => pe is > 0m) ?? 0;
        return positive < Math.Max(minPeers, 1)
            ? new CoverageSection(VerdictCoverageGap.FeedLimit, $"{positive} of {minPeers} peers with a forward P/E")
            : new CoverageSection(null, $"{positive} peers");
    }

    private async Task<(CoverageSection Section, IReadOnlyList<FundamentalFact>? Facts)> ReadFundamentalsAsync(
        string symbol, bool isCrypto, ScorecardValuationAxis axis, CancellationToken ct)
    {
        if (isCrypto)
        {
            return (new CoverageSection(VerdictCoverageGap.NotApplicable, "crypto"), null);
        }

        // The scorecard skips the fundamentals read for a name whose valuation does not apply; so does the audit.
        if (axis.NotApplicable)
        {
            return (new CoverageSection(VerdictCoverageGap.NotApplicable, "not read: valuation not applicable"), null);
        }

        var read = await TryAsync(
            symbol, "fundamentals", () => fundamentals.GetFundamentalsAsync(symbol, FundamentalsScorer.FactsPerConcept, ct), ct);
        if (read.Failure is not null)
        {
            return (Threw(read.Failure), null);
        }

        var result = read.Value;
        var coverage = result.Coverage;
        var section = coverage.Status switch
        {
            FundamentalsCoverageStatus.Covered when coverage.Bases.Count > 0 && coverage.Bases.All(b => b.Stale) =>
                new CoverageSection(VerdictCoverageGap.FeedLimit, "every period basis is older than the freshness window"),
            FundamentalsCoverageStatus.Covered => new CoverageSection(null, $"{result.Facts.Count} facts"),
            FundamentalsCoverageStatus.UnsupportedIssuerType => new CoverageSection(VerdictCoverageGap.NotApplicable, coverage.IssuerType),
            FundamentalsCoverageStatus.NoSourceAvailable when coverage.IssuerType == FundamentalsIssuerType.NotSecRegistrant =>
                new CoverageSection(VerdictCoverageGap.SymbolMapping, "not in the SEC ticker map"),
            _ => new CoverageSection(VerdictCoverageGap.FeedLimit, coverage.ErrorCode ?? coverage.Status),
        };
        return (section, result.Facts);
    }

    private async Task<(ScorecardResult? Card, CoverageVerdict Verdict)> ReadVerdictAsync(
        string symbol,
        ScorecardValuationAxis axis,
        IReadOnlyList<FundamentalFact>? facts,
        string? sector,
        string? industry,
        IReadOnlyList<decimal?>? peerPes,
        bool isCrypto,
        CancellationToken ct)
    {
        // Same call the dossier makes: a name whose valuation does not apply reads no facts, a crypto position no look-through.
        var read = await TryAsync(
            symbol,
            "scorecard",
            () => scorecards.ReadAsync(symbol, axis, axis.NotApplicable ? null : facts, ct, sector, industry, peerPes, lookThrough: !isCrypto),
            ct);
        if (read.Failure is not null)
        {
            return (null, new CoverageVerdict(null, null, null, VerdictCoverageGap.ScorecardThrew, read.Failure));
        }

        var card = read.Value;
        return (card, new CoverageVerdict(card.Verdict.Word, card.Verdict.FiredRuleId, card.Verdict.WaitReason, null));
    }

    private static CoverageSection LookThroughSection(
        bool isCrypto, ScorecardValuationAxis axis, ScorecardResult? card, CoverageVerdict verdict)
    {
        if (isCrypto)
        {
            return new CoverageSection(VerdictCoverageGap.NotApplicable, "crypto");
        }

        if (card is null)
        {
            return new CoverageSection(null, $"not read: {verdict.Gap}");
        }

        return card.LookThrough switch
        {
            null when axis.NotApplicable => new CoverageSection(
                VerdictCoverageGap.NotApplicable, "not a fund, or the holdings source could not tell"),
            null => new CoverageSection(VerdictCoverageGap.NotApplicable, "single name"),
            { Status: ScorecardLookThrough.Complete } basis => new CoverageSection(null, $"{basis.Holdings.Count} holdings from {basis.Source}"),
            { Status: ScorecardLookThrough.Partial } basis => new CoverageSection(VerdictCoverageGap.Partial, basis.Note),
            _ => new CoverageSection(VerdictCoverageGap.NoHoldings, "no source listed the fund's holdings"),
        };
    }

    private static CoverageSection Threw(string exceptionType) => new(VerdictCoverageGap.SectionThrew, exceptionType);

    /// <summary>
    /// Runs one section's read. An exception becomes the section's <c>SectionThrew</c> state (the type name only, never
    /// the message) and is logged, so one bad symbol never fails the audit; cancellation still propagates.
    /// </summary>
    private async Task<Attempt<T>> TryAsync<T>(string symbol, string section, Func<Task<T>> read, CancellationToken ct)
    {
        try
        {
            return new Attempt<T>(await read(), null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Verdict-coverage audit: the {Section} read for {Symbol} threw", section, symbol);
            return new Attempt<T>(default!, ex.GetType().Name);
        }
    }

    private sealed record Attempt<T>(T Value, string? Failure);

    private sealed record Candidate(string Symbol, List<string> Origins);

    private sealed class Candidates
    {
        private readonly Dictionary<string, Candidate> bySymbol = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<Candidate> ordered = [];

        public IReadOnlyList<Candidate> Ordered => ordered;

        public void Add(IEnumerable<string> symbols, string origin)
        {
            foreach (var raw in symbols)
            {
                var symbol = raw.Trim().ToUpperInvariant();
                if (symbol.Length == 0)
                {
                    continue;
                }

                if (!bySymbol.TryGetValue(symbol, out var candidate))
                {
                    candidate = new Candidate(symbol, []);
                    bySymbol[symbol] = candidate;
                    ordered.Add(candidate);
                }

                if (!candidate.Origins.Contains(origin))
                {
                    candidate.Origins.Add(origin);
                }
            }
        }
    }
}
