namespace FinanceSentry.Modules.Wealth.Infrastructure.Jobs;

using FinanceSentry.Core.Domain;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Utils;
using Hangfire;
using Microsoft.Extensions.Logging;

public class NetWorthSnapshotJob(
    IBankingTotalsReader bankingTotals,
    ICryptoHoldingsReader cryptoReader,
    IBrokerageHoldingsReader brokerageReader,
    INetWorthSnapshotService snapshotService,
    IBookFiguresService bookFigures,
    ILogger<NetWorthSnapshotJob>? logger = null)
{
    private readonly ILogger<NetWorthSnapshotJob>? _logger = logger;
    private readonly IBankingTotalsReader _bankingTotals = bankingTotals ?? throw new ArgumentNullException(nameof(bankingTotals));
    private readonly ICryptoHoldingsReader _cryptoReader = cryptoReader ?? throw new ArgumentNullException(nameof(cryptoReader));
    private readonly IBrokerageHoldingsReader _brokerageReader = brokerageReader ?? throw new ArgumentNullException(nameof(brokerageReader));
    private readonly INetWorthSnapshotService _snapshotService = snapshotService ?? throw new ArgumentNullException(nameof(snapshotService));
    private readonly IBookFiguresService _bookFigures = bookFigures ?? throw new ArgumentNullException(nameof(bookFigures));

    [AutomaticRetry(Attempts = 2)]
    public async Task ExecuteAsync(CancellationToken ct = default)
        => await CaptureAllUsersAsync(DateOnly.FromDateTime(DateTime.UtcNow), ct);

    [AutomaticRetry(Attempts = 2)]
    public async Task ExecuteForUserAsync(Guid userId, CancellationToken ct = default)
        => await CaptureForUserAsync(userId, DateOnly.FromDateTime(DateTime.UtcNow), ct);

    public async Task CaptureAllUsersAsync(DateOnly snapshotDate, CancellationToken ct = default)
    {
        var userIds = await _bankingTotals.GetActiveUserIdsAsync(ct);
        foreach (var userId in userIds)
            await CaptureForUserAsync(userId, snapshotDate, ct);
    }

    public async Task CaptureForUserAsync(Guid userId, DateOnly snapshotDate, CancellationToken ct = default)
        => await TakeSnapshotAsync(userId, snapshotDate, ct);

    // A sleeve is only trusted if its freshest holding synced within this window; older
    // than this (a disconnected provider, a stuck gateway, a failed sync) is treated as
    // stale and carried forward rather than counted as the live number.
    private static readonly TimeSpan StaleWindow = TimeSpan.FromHours(36);

    // The source ids of the three sleeves the snapshot's columns hold (WealthSources); any other registered
    // source gets its per-source row only.
    private const string BankingSourceId = "banking";
    private const string BrokerageSourceId = "brokerage";
    private const string CryptoSourceId = "crypto";

    private async Task TakeSnapshotAsync(Guid userId, DateOnly snapshotDate, CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        // One read of every registered source: the sleeve totals, the invested split, the conversion status and
        // the per-source rows below are all taken from it, so the rows add up to the columns. The holdings and
        // sync-time reads that follow only decide freshness - the readings carry no sync timestamp.
        var book = await _bookFigures.ReadAsync(userId, ct);

        var banking = book.Sources.FirstOrDefault(s => s.SourceId == BankingSourceId)?.Reading;
        var bankingTotal = banking?.TotalValueUsd ?? 0m;
        // Banking is fresh only if a successful sync landed within the window; a lapsed
        // connection (stale Revolut/AIB) must carry forward, not record a phantom drop.
        var bankingLastSync = await _bankingTotals.GetLatestSuccessfulSyncAsync(userId, ct);
        var bankingFresh = banking is not null && (bankingLastSync is null
            ? bankingTotal == 0m
            : bankingLastSync.Value >= now - StaleWindow);

        var crypto = book.Sources.FirstOrDefault(s => s.SourceId == CryptoSourceId)?.Reading;
        var cryptoTotal = crypto?.TotalValueUsd ?? 0m;
        var cryptoHoldings = await _cryptoReader.GetHoldingsAsync(userId, ct);
        var cryptoFresh = crypto is not null
            && cryptoHoldings.Count > 0
            && cryptoHoldings.Max(h => h.SyncedAt) >= now - StaleWindow;

        var brokerage = book.Sources.FirstOrDefault(s => s.SourceId == BrokerageSourceId)?.Reading;
        var brokerageTotal = brokerage?.TotalValueUsd ?? 0m;
        var brokerageHoldings = await _brokerageReader.GetHoldingsAsync(userId, ct);
        var brokerageFresh = brokerage is not null
            && brokerageHoldings.Count > 0
            && brokerageHoldings.Max(h => h.SyncedAt) >= now - StaleWindow;

        // The invested part of each sleeve comes from the canonical book figures, so the stored cash is the
        // dashboard's "cash" for the same moment. A stale source would make the split partial, so it is
        // withheld (null), as the dashboard hides its sub-line.
        var measured = !book.IsStale;
        var sleevePositions = book.Sources
            .Where(s => WealthSourceIds.Sleeves.Contains(s.SourceId))
            .SelectMany(s => s.Reading?.Positions ?? [])
            .ToList();
        decimal? brokerageInvested = measured
            ? sleevePositions.Where(p => p.AssetClass != AssetClassNormalizer.Crypto).Sum(p => p.UsdValue)
            : null;
        decimal? cryptoInvested = measured
            ? sleevePositions.Where(p => p.AssetClass == AssetClassNormalizer.Crypto).Sum(p => p.UsdValue)
            : null;

        // The worst conversion status of what this snapshot was summed from, which is what the book summed.
        var fxStatus = book.FxStatus;
        if (fxStatus.ToReason() is { } fxReason)
            _logger?.LogWarning(
                "Net worth snapshot for user {UserId} on {SnapshotDate} used a substituted FX rate ({FxStatus}, {Reason})",
                userId, snapshotDate, fxStatus, fxReason);

        await _snapshotService.PersistSnapshotAsync(userId, new NetWorthSnapshotData(
            snapshotDate, bankingTotal, brokerageTotal, cryptoTotal,
            BankingFresh: bankingFresh, BrokerageFresh: brokerageFresh, CryptoFresh: cryptoFresh,
            BrokerageInvested: brokerageInvested, CryptoInvested: cryptoInvested,
            FxStatus: fxStatus,
            Sources: book.Sources), ct);
    }
}
