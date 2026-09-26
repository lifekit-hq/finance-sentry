using FinanceSentry.Modules.BrokerageSync.Domain;
using FinanceSentry.Modules.BrokerageSync.Domain.Repositories;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.IBKR.Flex;
using Microsoft.Extensions.Logging;

namespace FinanceSentry.Modules.BrokerageSync.Application.Services;

public interface IIbkrFlexTradeSyncService
{
    /// <summary>
    /// Pulls a Flex statement for <paramref name="userId"/> over <paramref name="window"/> (or
    /// the saved query's own default range when <c>null</c>) and persists its trades and cash
    /// transactions idempotently. A no-op — logged, not thrown — when the user has no active
    /// Flex credential, since configuring one is a manual step the human has not necessarily
    /// taken yet.
    /// </summary>
    Task<IbkrFlexTradeSyncResult> SyncAsync(Guid userId, FlexStatementWindow? window = null, CancellationToken ct = default);
}

public sealed record IbkrFlexTradeSyncResult(int TradesUpserted, int CashTransactionsUpserted)
{
    public static readonly IbkrFlexTradeSyncResult NoCredential = new(0, 0);
}

/// <summary>
/// Orchestrates one Flex statement pull into persisted trades/cash transactions
/// (fs-435 S5, PR2). Every instrument a trade references is upserted by conid first —
/// mirroring <c>SyncIBKRHoldingsCommandHandler</c>'s instrument-linking pattern — so a
/// trade always resolves to a durable <see cref="BrokerageInstrument"/> row without ever
/// touching its classification.
/// </summary>
public sealed class IbkrFlexTradeSyncService(
    IIbkrFlexStatementFetcher statementFetcher,
    IBrokerageInstrumentRepository instrumentRepository,
    IBrokerageTradeRepository tradeRepository,
    IBrokerageCashTransactionRepository cashTransactionRepository,
    ILogger<IbkrFlexTradeSyncService> logger) : IIbkrFlexTradeSyncService
{
    private const string Provider = "ibkr";

    public async Task<IbkrFlexTradeSyncResult> SyncAsync(
        Guid userId, FlexStatementWindow? window = null, CancellationToken ct = default)
    {
        var statement = await statementFetcher.FetchAsync(userId, window, ct);
        if (statement is null)
        {
            // IbkrFlexStatementFetcher already logs the no-credential case.
            return IbkrFlexTradeSyncResult.NoCredential;
        }

        var instrumentByConid = await UpsertInstrumentsAsync(userId, statement, ct);
        var tradesUpserted = await UpsertTradesAsync(userId, statement.Trades, instrumentByConid, ct);
        var cashTransactionsUpserted = await UpsertCashTransactionsAsync(userId, statement.CashTransactions, ct);

        logger.LogInformation(
            "Synced IBKR Flex statement for user {UserId}: {TradeCount} trades, {CashCount} cash transactions.",
            userId, tradesUpserted, cashTransactionsUpserted);

        return new IbkrFlexTradeSyncResult(tradesUpserted, cashTransactionsUpserted);
    }

    /// <summary>
    /// Upserts a <see cref="BrokerageInstrument"/> per distinct conid on the statement's
    /// trades, preferring identity fields from the Financial Instrument Information section
    /// (richer/more authoritative) and falling back to the trade row itself.
    /// </summary>
    private async Task<Dictionary<long, BrokerageInstrument>> UpsertInstrumentsAsync(
        Guid userId, FlexStatementXml statement, CancellationToken ct)
    {
        var instrumentByConid = new Dictionary<long, BrokerageInstrument>();

        var financialInstrumentByConid = statement.FinancialInstruments
            .Select(fi => (Conid: IbkrFlexMapper.ParseConid(fi.Conid), Instrument: fi))
            .Where(x => x.Conid is not null)
            .ToDictionary(x => x.Conid!.Value, x => x.Instrument);

        foreach (var conid in statement.Trades
                     .Select(t => IbkrFlexMapper.ParseConid(t.Conid))
                     .Where(c => c is not null)
                     .Select(c => c!.Value)
                     .Distinct())
        {
            if (instrumentByConid.ContainsKey(conid))
                continue;

            financialInstrumentByConid.TryGetValue(conid, out var financialInstrument);
            var trade = statement.Trades.First(t => IbkrFlexMapper.ParseConid(t.Conid) == conid);

            var symbol = financialInstrument?.Symbol ?? trade.Symbol ?? string.Empty;
            var instrumentType = financialInstrument?.AssetCategory ?? trade.AssetCategory ?? string.Empty;
            var isin = financialInstrument?.Isin ?? trade.Isin;

            var instrument = await instrumentRepository.GetByConidAsync(userId, Provider, conid, ct);
            if (instrument is null)
            {
                instrument = new BrokerageInstrument(userId, Provider, conid, symbol, instrumentType, isin);
                await instrumentRepository.AddAsync(instrument, ct);
            }
            else
            {
                instrument.RefreshIdentity(symbol, instrumentType, isin);
                instrumentRepository.Update(instrument);
            }

            instrumentByConid[conid] = instrument;
        }

        if (instrumentByConid.Count > 0)
            await instrumentRepository.SaveChangesAsync(ct);

        return instrumentByConid;
    }

    private async Task<int> UpsertTradesAsync(
        Guid userId,
        IReadOnlyList<FlexTradeXml> trades,
        IReadOnlyDictionary<long, BrokerageInstrument> instrumentByConid,
        CancellationToken ct)
    {
        var upserted = 0;

        foreach (var raw in trades)
        {
            if (string.IsNullOrWhiteSpace(raw.IbExecutionId))
            {
                logger.LogWarning(
                    "Skipping IBKR Flex trade for user {UserId} with no ibExecID (tradeID {TradeId}).",
                    userId, raw.TradeId);
                continue;
            }

            var conid = IbkrFlexMapper.ParseConid(raw.Conid);
            var instrumentId = conid is not null && instrumentByConid.TryGetValue(conid.Value, out var instrument)
                ? instrument.Id
                : (Guid?)null;

            var existing = await tradeRepository.GetByExecutionIdAsync(userId, Provider, raw.IbExecutionId, ct);
            if (existing is null)
            {
                await tradeRepository.AddAsync(IbkrFlexMapper.CreateTrade(userId, Provider, raw, instrumentId), ct);
            }
            else
            {
                IbkrFlexMapper.RefreshTrade(existing, raw, instrumentId);
                tradeRepository.Update(existing);
            }

            upserted++;
        }

        if (upserted > 0)
            await tradeRepository.SaveChangesAsync(ct);

        return upserted;
    }

    private async Task<int> UpsertCashTransactionsAsync(
        Guid userId, IReadOnlyList<FlexCashTransactionXml> transactions, CancellationToken ct)
    {
        var upserted = 0;

        foreach (var raw in transactions)
        {
            var idempotencyKey = IbkrFlexMapper.ComputeCashTransactionIdempotencyKey(raw);
            var existing = await cashTransactionRepository.GetByIdempotencyKeyAsync(userId, Provider, idempotencyKey, ct);
            if (existing is not null)
                continue;

            await cashTransactionRepository.AddAsync(IbkrFlexMapper.CreateCashTransaction(userId, Provider, raw), ct);
            upserted++;
        }

        if (upserted > 0)
            await cashTransactionRepository.SaveChangesAsync(ct);

        return upserted;
    }
}
