namespace FinanceSentry.Modules.Research.Application.Services.Etf;

using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Domain.Scoring;
using Microsoft.Extensions.Logging;

/// <summary>Scores one holding with the single-name rules, given the valuation axis already read for it.</summary>
public delegate Task<ScorecardResult> ScoreSingleName(
    string symbol,
    ScorecardValuationAxis valuation,
    string? sector,
    string? industry,
    IReadOnlyList<decimal?>? peerForwardPes,
    CancellationToken ct);

/// <summary>An ETF's look-through: the published basis and the BUY-or-better share the ETF table reads.</summary>
public sealed record EtfLookThroughRead(ScorecardLookThrough Basis, decimal? BuyWeightSharePct);

/// <summary>
/// Looks an ETF through to its top 10 holdings (issue #1324): reads the holdings (<see cref="IFundHoldingsSource"/>),
/// scores each with the same single-name rules a stock gets, and totals the weight reading BUY or better. Holdings are
/// scored one after another because the structure and fundamentals reads share the request's one scoped DbContext. A
/// holding is scored without a look-through of its own, so a fund of funds can never recurse. A holding that cannot be
/// scored counts against the fund and the basis says so; a fund with no holdings at all says that instead.
/// </summary>
public sealed class EtfLookThroughReader(
    IFundHoldingsSource holdings,
    ScorecardValuationReader valuation,
    ILogger<EtfLookThroughReader> logger)
{
    public const string UnavailableNote =
        "Holdings for this fund could not be read from Yahoo or an issuer file, so the look-through was not applied: "
        + "the word cannot reach BUY, and AVOID still applies to the fund's own structure.";

    /// <summary>
    /// The look-through for <paramref name="fund"/>, or null when the symbol is not an ETF (or the source could not tell),
    /// in which case the caller keeps its single-name behaviour.
    /// </summary>
    public async Task<EtfLookThroughRead?> ReadAsync(
        string fund, ScoreSingleName score, decimal buyShareRequiredPct, CancellationToken ct)
    {
        var lookup = await holdings.GetTopHoldingsAsync(fund, ct);
        if (lookup.Kind != FundKind.Fund)
        {
            return null;
        }

        if (lookup.Holdings.Count == 0)
        {
            return new EtfLookThroughRead(
                new ScorecardLookThrough(
                    ScorecardLookThrough.Unavailable, null, null, 0m, 0m, null, buyShareRequiredPct, [], UnavailableNote),
                null);
        }

        var rows = new List<LookThroughHolding>(lookup.Holdings.Count);
        var weighted = new List<WeightedWord>(lookup.Holdings.Count);
        foreach (var holding in lookup.Holdings)
        {
            var (word, firedRule) = await ScoreAsync(fund, holding, score, ct);
            rows.Add(new LookThroughHolding(holding.Symbol, holding.Name, holding.WeightPct, word?.ToString(), firedRule));
            weighted.Add(new WeightedWord(holding.WeightPct, word));
        }

        var unscored = weighted.Count(w => w.Word is null or VerdictWord.NotScored);
        var share = LookThroughWeights.BuyWeightSharePct(weighted);
        var complete = unscored == 0 && rows.Count >= FundHoldingsLookup.TopCount;
        return new EtfLookThroughRead(
            new ScorecardLookThrough(
                complete ? ScorecardLookThrough.Complete : ScorecardLookThrough.Partial,
                lookup.Source,
                lookup.AsOf,
                LookThroughWeights.Covered(weighted),
                LookThroughWeights.BuyWeight(weighted),
                share,
                buyShareRequiredPct,
                rows,
                complete ? null : PartialNote(rows.Count, unscored)),
            share);
    }


    private static string PartialNote(int listed, int unscored)
    {
        var notes = new List<string>();
        if (listed < FundHoldingsLookup.TopCount)
        {
            notes.Add($"Only {listed} holdings were listed.");
        }

        if (unscored > 0)
        {
            notes.Add($"{unscored} of {listed} listed holdings could not be scored and count against the fund.");
        }

        return string.Join(' ', notes);
    }

    private async Task<(VerdictWord? Word, string? FiredRuleId)> ScoreAsync(
        string fund, FundHolding holding, ScoreSingleName score, CancellationToken ct)
    {
        if (holding.Symbol is null || holding.Symbol.Equals(fund, StringComparison.OrdinalIgnoreCase))
        {
            return (null, null);
        }

        try
        {
            var (axis, sector, industry, peerForwardPes) = await valuation.ReadAsync(holding.Symbol, ct);
            var card = await score(holding.Symbol, axis, sector, industry, peerForwardPes, ct);
            return (Enum.TryParse<VerdictWord>(card.Verdict.Word, out var word) ? word : null, card.Verdict.FiredRuleId);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Look-through holding {Holding} of {Fund} could not be scored", holding.Symbol, fund);
            return (null, null);
        }
    }
}
