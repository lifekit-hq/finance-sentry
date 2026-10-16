using System.Globalization;
using FinanceSentry.Modules.CryptoSync.Domain;

namespace FinanceSentry.Modules.CryptoSync.Application.Services;

/// <summary>A coin sent by one venue and received by another: one move inside the sleeve.</summary>
public sealed record CryptoTransferPair(CryptoFlowEvent Sent, CryptoFlowEvent Received)
{
    /// <summary>What the move cost in the sent unit: what left the sending venue less what arrived.</summary>
    public decimal Fee => Math.Max(0m, CryptoFlowClassifier.SentGross(Sent) - Received.Amount);
}

/// <summary>
/// Pairs transfer legs across venues (S2b; F2 matching tiers). A leg is a coin crossing a chain
/// (<see cref="CryptoFlowClassifier.ChainTransfer"/>); a pair is a sent leg on one venue and a received leg on another,
/// same unit. Tier 1: the same chain hash, the received no more than the sent, at any distance in time. Tier 2, when
/// at least one side names no hash: the same network where both name one, received from an hour before to a day after
/// the send, within the fee tolerance, and each the other's only candidate - an ambiguous leg stays unmatched rather
/// than paired by guess.
/// </summary>
public static class CryptoTransferMatcher
{
    /// <summary>How long before the send a receipt may be dated: the venues' clocks and booking lag.</summary>
    public static readonly TimeSpan EarlyReceipt = TimeSpan.FromHours(1);

    /// <summary>How long after the send a receipt may arrive and still match without a hash.</summary>
    public static readonly TimeSpan ArrivalWindow = TimeSpan.FromHours(24);

    /// <summary>The share of the amount moved that may go missing beyond the reported fee (network fees, rounding).</summary>
    public const decimal FeeTolerance = 0.005m;

    // A Binance transfer between Binance accounts carries this in place of a hash; it never crossed a chain.
    private const string OffChainPrefix = "Off-chain transfer";

    // The venues name a chain in different words (Binance SOL, Revolut X SOLANA). Names outside this map are compared
    // only with themselves, never ruled out against each other.
    // shortcut: a small alias map of the common chains, not a full registry of either venue's network codes.
    private static readonly Dictionary<string, string> NetworkAliases = new(StringComparer.Ordinal)
    {
        ["BITCOIN"] = "BTC",
        ["ETHEREUM"] = "ETH",
        ["ERC20"] = "ETH",
        ["SOLANA"] = "SOL",
        ["TRON"] = "TRX",
        ["TRC20"] = "TRX",
        ["BNBSMARTCHAIN"] = "BSC",
        ["BEP20"] = "BSC",
        ["POLYGON"] = "MATIC",
        ["POL"] = "MATIC",
        ["ARBITRUMONE"] = "ARBITRUM",
        ["OPTIMISM"] = "OP",
        ["AVALANCHE"] = "AVAXC",
        ["AVALANCHECCHAIN"] = "AVAXC",
        ["LITECOIN"] = "LTC",
        ["DOGECOIN"] = "DOGE",
        ["RIPPLE"] = "XRP",
        ["CARDANO"] = "ADA",
        ["POLKADOT"] = "DOT",
    };

    private static readonly HashSet<string> KnownNetworks = [.. NetworkAliases.Values];

    public static IReadOnlyList<CryptoTransferPair> Match(IEnumerable<CryptoFlowEvent> rows)
    {
        var legs = rows
            .Select(Leg)
            .OfType<TransferLeg>()
            .ToList();
        var sent = legs.Where(l => l.Direction == CryptoTransferDirection.Out).ToList();
        var received = legs.Where(l => l.Direction == CryptoTransferDirection.In).ToList();
        var pairs = new List<CryptoTransferPair>();
        var paired = new HashSet<Guid>();

        // Tier 1: one send and one receipt carry the hash; a hash shared by more legs is not guessed at.
        foreach (var send in sent.Where(s => s.Hash is not null))
        {
            var candidates = received.Where(r => r.Hash == send.Hash && IsCandidate(send, r, AmountWithinSent)).ToList();
            if (candidates.Count == 1
                && sent.Count(s => s.Hash == send.Hash && IsCandidate(s, candidates[0], AmountWithinSent)) == 1)
            {
                pairs.Add(new CryptoTransferPair(send.Row, candidates[0].Row));
                paired.Add(send.Row.Id);
                paired.Add(candidates[0].Row.Id);
            }
        }

        // Tier 2: a side without a hash, matched on unit, network, time and amount, and only when unique both ways.
        var openSent = sent.Where(s => !paired.Contains(s.Row.Id)).ToList();
        var openReceived = received.Where(r => !paired.Contains(r.Row.Id)).ToList();
        foreach (var send in openSent)
        {
            var candidates = openReceived.Where(r => IsHashlessCandidate(send, r)).ToList();
            if (candidates.Count != 1)
            {
                continue;
            }

            var receipt = candidates[0];
            if (openSent.Count(s => IsHashlessCandidate(s, receipt)) == 1)
            {
                pairs.Add(new CryptoTransferPair(send.Row, receipt.Row));
            }
        }

        return pairs;
    }

    /// <summary>
    /// A chain hash in one comparable form: a hex hash lowercased with one leading <c>0x</c> dropped (venues differ in
    /// both); any other (Solana's base58) kept as given, case and all. Null for none or a Binance off-chain id.
    /// </summary>
    public static string? NormalizeHash(string? txRef)
    {
        var hash = txRef?.Trim();
        if (string.IsNullOrEmpty(hash) || hash.StartsWith(OffChainPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var hex = hash.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? hash[2..] : hash;
        return hex.Length > 0 && hex.All(char.IsAsciiHexDigit) ? hex.ToLowerInvariant() : hash;
    }

    /// <summary>Whether two venues' network names can be the same chain: equal once aliased, or not both known.</summary>
    public static bool NetworksCompatible(string? a, string? b)
    {
        if (Canonical(a) is not { } left || Canonical(b) is not { } right)
        {
            return true;
        }

        return left == right || !(KnownNetworks.Contains(left) && KnownNetworks.Contains(right));
    }

    /// <summary>
    /// The F2 amount test: the received no more than the sent, and the shortfall within the reported fee plus the
    /// tolerance (or the tolerance alone when no fee is reported).
    /// </summary>
    public static bool AmountsMatch(CryptoFlowEvent sent, CryptoFlowEvent received)
    {
        var moved = CryptoFlowClassifier.SentGross(sent);
        if (received.Amount > moved)
        {
            return false;
        }

        return moved - received.Amount <= CryptoFlowClassifier.SameUnitFee(sent) + (FeeTolerance * moved);
    }

    private static bool AmountWithinSent(CryptoFlowEvent sent, CryptoFlowEvent received) =>
        received.Amount <= CryptoFlowClassifier.SentGross(sent);

    private static bool IsHashlessCandidate(TransferLeg send, TransferLeg receipt) =>
        (send.Hash is null || receipt.Hash is null)
        && IsCandidate(send, receipt, AmountsMatch)
        && NetworksCompatible(send.Row.Network, receipt.Row.Network)
        && receipt.Row.OccurredAt >= send.Row.OccurredAt - EarlyReceipt
        && receipt.Row.OccurredAt <= send.Row.OccurredAt + ArrivalWindow;

    private static bool IsCandidate(
        TransferLeg send,
        TransferLeg receipt,
        Func<CryptoFlowEvent, CryptoFlowEvent, bool> amountTest) =>
        !string.Equals(send.Row.Provider, receipt.Row.Provider, StringComparison.Ordinal)
        && string.Equals(send.Row.Unit, receipt.Row.Unit, StringComparison.OrdinalIgnoreCase)
        && amountTest(send.Row, receipt.Row);

    private static TransferLeg? Leg(CryptoFlowEvent row)
    {
        if (CryptoFlowClassifier.ChainTransfer(row) is not { } direction)
        {
            return null;
        }

        var raw = row.TxRef?.Trim();
        if (raw is not null && raw.StartsWith(OffChainPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return new TransferLeg(row, direction, NormalizeHash(raw));
    }

    private static string? Canonical(string? network)
    {
        if (string.IsNullOrWhiteSpace(network))
        {
            return null;
        }

        var key = new string([.. network.Where(char.IsAsciiLetterOrDigit)]).ToUpper(CultureInfo.InvariantCulture);
        return NetworkAliases.TryGetValue(key, out var alias) ? alias : key;
    }

    private sealed record TransferLeg(CryptoFlowEvent Row, CryptoTransferDirection Direction, string? Hash);
}
