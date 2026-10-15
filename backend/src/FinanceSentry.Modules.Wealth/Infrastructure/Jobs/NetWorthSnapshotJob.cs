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
    IEnumerable<IWealthSource> sources,
    ILogger<NetWorthSnapshotJob>? logger = null)
{
    private readonly ILogger<NetWorthSnapshotJob>? _logger = logger;
    private readonly IBankingTotalsReader _bankingTotals = bankingTotals ?? throw new ArgumentNullException(nameof(bankingTotals));
    private readonly ICryptoHoldingsReader _cryptoReader = cryptoReader ?? throw new ArgumentNullException(nameof(cryptoReader));
    private readonly IBrokerageHoldingsReader _brokerageReader = brokerageReader ?? throw new ArgumentNullException(nameof(brokerageReader));
    private readonly INetWorthSnapshotService _snapshotService = snapshotService ?? throw new ArgumentNullException(nameof(snapshotService));
    private readonly IBookFiguresService _bookFigures = bookFigures ?? throw new ArgumentNullException(nameof(bookFigures));
    private readonly IReadOnlyList<IWealthSource> _sources = (sources ?? throw new ArgumentNullException(nameof(sources))).ToList();

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

        // The worst conversion status of what this snapshot was summed from: the book figures cover the
        // bank accounts, the two holdings reads this run's own sleeve totals.
        var fxStatus = book.FxStatus;
        foreach (var h in cryptoHoldings.Where(h => h.FreeQuantity + h.LockedQuantity != 0m))
            fxStatus = fxStatus.Worst(h.FxStatus);
        foreach (var h in brokerageHoldings.Where(h => h.Quantity != 0m))
            fxStatus = fxStatus.Worst(h.FxStatus);

        if (fxStatus.ToReason() is { } fxReason)
            _logger?.LogWarning(
                "Net worth snapshot for user {UserId} on {SnapshotDate} used a substituted FX rate ({FxStatus}, {Reason})",
                userId, snapshotDate, fxStatus, fxReason);

        var sourceFigures = await ReadSourcesAsync(userId, ct);

        await _snapshotService.PersistSnapshotAsync(userId, new NetWorthSnapshotData(
            snapshotDate, bankingTotal, brokerageTotal, cryptoTotal,
            BankingFresh: bankingFresh, BrokerageFresh: brokerageFresh, CryptoFresh: cryptoFresh,
            BrokerageInvested: brokerageInvested, CryptoInvested: cryptoInvested,
            FxStatus: fxStatus,
            Sources: sourceFigures), ct);
    }

    // One figure per registered source, in registration order, so a source added to the registry gets its row with
    // no change here. A source that throws is recorded as unread (its row says so) rather than left out, which is
    // how the book figures treat it: it contributes nothing and is reported stale.
    private async Task<IReadOnlyList<SourceSnapshotFigure>> ReadSourcesAsync(Guid userId, CancellationToken ct)
    {
        var figures = new List<SourceSnapshotFigure>(_sources.Count);
        foreach (var source in _sources)
        {
            WealthSourceReading? reading = null;
            try
            {
                reading = await source.ReadAsync(userId, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger?.LogWarning(ex,
                    "Wealth source {SourceId} unavailable for user {UserId}; its snapshot row records no figures.",
                    source.SourceId, userId);
            }

            figures.Add(new SourceSnapshotFigure(source.SourceId, source.Kind, reading));
        }

        return figures;
    }
}
