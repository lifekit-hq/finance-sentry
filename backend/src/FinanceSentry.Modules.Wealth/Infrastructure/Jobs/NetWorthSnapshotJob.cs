namespace FinanceSentry.Modules.Wealth.Infrastructure.Jobs;

using FinanceSentry.Core.Domain;
using FinanceSentry.Core.Interfaces;
using Hangfire;

public class NetWorthSnapshotJob(
    IBankingTotalsReader bankingTotals,
    ICryptoHoldingsReader cryptoReader,
    IBrokerageHoldingsReader brokerageReader,
    INetWorthSnapshotService snapshotService,
    IBookFiguresService bookFigures)
{
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

    private async Task TakeSnapshotAsync(Guid userId, DateOnly snapshotDate, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var bankingTotal = await _bankingTotals.GetTotalUsdAsync(userId, ct);
        // Banking is fresh only if a successful sync landed within the window; a lapsed
        // connection (stale Revolut/AIB) must carry forward, not record a phantom drop.
        var bankingLastSync = await _bankingTotals.GetLatestSuccessfulSyncAsync(userId, ct);
        var bankingFresh = bankingLastSync is null
            ? bankingTotal == 0m
            : bankingLastSync.Value >= now - StaleWindow;

        var cryptoHoldings = await _cryptoReader.GetHoldingsAsync(userId, ct);
        var cryptoTotal = cryptoHoldings.Sum(h => h.UsdValue);
        var cryptoFresh = cryptoHoldings.Count > 0
            && cryptoHoldings.Max(h => h.SyncedAt) >= now - StaleWindow;

        var brokerageHoldings = await _brokerageReader.GetHoldingsAsync(userId, ct);
        var brokerageTotal = brokerageHoldings.Sum(h => h.UsdValue);
        var brokerageFresh = brokerageHoldings.Count > 0
            && brokerageHoldings.Max(h => h.SyncedAt) >= now - StaleWindow;

        // The invested part of each sleeve comes from the canonical book figures, so the stored cash is the
        // dashboard's "cash" for the same moment. A stale source would make the split partial, so it is
        // withheld (null), as the dashboard hides its sub-line.
        var book = await _bookFigures.ReadAsync(userId, ct);
        var measured = !book.IsStale;
        decimal? brokerageInvested = measured
            ? book.Positions.Where(p => p.AssetClass != AssetClassNormalizer.Crypto).Sum(p => p.UsdValue)
            : null;
        decimal? cryptoInvested = measured
            ? book.Positions.Where(p => p.AssetClass == AssetClassNormalizer.Crypto).Sum(p => p.UsdValue)
            : null;

        await _snapshotService.PersistSnapshotAsync(userId, new NetWorthSnapshotData(
            snapshotDate, bankingTotal, brokerageTotal, cryptoTotal,
            BankingFresh: bankingFresh, BrokerageFresh: brokerageFresh, CryptoFresh: cryptoFresh,
            BrokerageInvested: brokerageInvested, CryptoInvested: cryptoInvested), ct);
    }
}
