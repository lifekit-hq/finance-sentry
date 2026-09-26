using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.BrokerageSync.Domain;
using FinanceSentry.Modules.BrokerageSync.Domain.Interfaces;
using FinanceSentry.Modules.BrokerageSync.Domain.Repositories;

namespace FinanceSentry.Modules.BrokerageSync.Application.Commands;

public sealed record SyncIBKRHoldingsCommand(Guid UserId) : ICommand<SyncIBKRHoldingsResult>;

public sealed record SyncIBKRHoldingsResult(int HoldingsCount, DateTime SyncedAt);

public sealed class SyncIBKRHoldingsCommandHandler : ICommandHandler<SyncIBKRHoldingsCommand, SyncIBKRHoldingsResult>
{
    private const string Provider = "ibkr";

    private readonly IIBKRCredentialRepository _credentialRepository;
    private readonly IBrokerageHoldingRepository _holdingRepository;
    private readonly IBrokerageInstrumentRepository _instrumentRepository;
    private readonly IBrokerAdapter _adapter;

    public SyncIBKRHoldingsCommandHandler(
        IIBKRCredentialRepository credentialRepository,
        IBrokerageHoldingRepository holdingRepository,
        IBrokerageInstrumentRepository instrumentRepository,
        IBrokerAdapter adapter)
    {
        _credentialRepository = credentialRepository;
        _holdingRepository = holdingRepository;
        _instrumentRepository = instrumentRepository;
        _adapter = adapter;
    }

    public async Task<SyncIBKRHoldingsResult> Handle(SyncIBKRHoldingsCommand request, CancellationToken ct)
    {
        var credential = await _credentialRepository.GetByUserIdAsync(request.UserId, ct)
            ?? throw new InvalidOperationException("No active IBKR credential found for this user.");

        try
        {
            await _adapter.EnsureSessionAsync(credential.Id, ct);

            var accountId = credential.AccountId
                ?? await _adapter.GetAccountIdAsync(credential.Id, ct);

            if (credential.AccountId is null)
            {
                credential.UpdateAccountId(accountId);
                _credentialRepository.Update(credential);
            }

            var positions = await _adapter.GetPositionsAsync(credential.Id, accountId, ct);

            var syncedAt = DateTime.UtcNow;

            // Ignore zero-quantity positions — IBKR keeps returning sold-out symbols
            // at qty 0, which we must not surface (or persist) as $0 holdings.
            var activePositions = positions
                .Where(p => p.Quantity != 0m)
                .ToList();

            var instrumentByConid = await UpsertInstrumentsAsync(request.UserId, activePositions, ct);

            var holdings = activePositions
                .Select(p => new BrokerageHolding(
                    request.UserId,
                    p.Symbol,
                    p.InstrumentType,
                    p.Quantity,
                    p.UsdValue,
                    "ibkr",
                    averageCostUsd: p.AverageCostUsd,
                    acquiredAt: p.AverageCostUsd.HasValue ? syncedAt : null,
                    instrumentId: p.Conid.HasValue ? instrumentByConid[p.Conid.Value].Id : null))
                .ToList();

            await _holdingRepository.UpsertRangeAsync(holdings, ct);
            await _holdingRepository.SaveChangesAsync(ct);

            var positionByKey = activePositions.ToDictionary(p => p.Symbol, StringComparer.Ordinal);
            var persisted = await _holdingRepository.GetByUserIdAsync(request.UserId, ct);

            // Reconcile: drop persisted holdings the user no longer holds (sold out /
            // no longer returned or now zero) so they leave the DB instead of lingering.
            // The linked BrokerageInstrument row is never touched here — it survives
            // a full exit so its human-set classification is not lost.
            var stale = persisted
                .Where(h => h.Provider == "ibkr" && !positionByKey.ContainsKey(h.Symbol))
                .ToList();
            if (stale.Count > 0)
            {
                _holdingRepository.RemoveRange(stale);
            }

            foreach (var h in persisted)
            {
                if (positionByKey.TryGetValue(h.Symbol, out var pos))
                {
                    h.SetCostBasis(
                        pos.AverageCostUsd,
                        pos.AverageCostUsd.HasValue ? (h.AcquiredAt ?? syncedAt) : h.AcquiredAt);
                }
            }
            await _holdingRepository.SaveChangesAsync(ct);

            credential.RecordSyncSuccess();
            _credentialRepository.Update(credential);
            await _credentialRepository.SaveChangesAsync(ct);

            return new SyncIBKRHoldingsResult(holdings.Count, DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            credential.RecordSyncError(ex.Message);
            _credentialRepository.Update(credential);
            await _credentialRepository.SaveChangesAsync(ct);
            throw;
        }
    }

    /// <summary>
    /// Upserts a <see cref="BrokerageInstrument"/> per distinct conid on the wire, keyed
    /// per user and broker instrument. Refreshes symbol identity but never touches
    /// <see cref="BrokerageInstrument.Classification"/> — that field is human-set only.
    /// Positions with no conid (e.g. synthetic cash positions) are skipped.
    /// </summary>
    private async Task<Dictionary<long, BrokerageInstrument>> UpsertInstrumentsAsync(
        Guid userId, IReadOnlyList<BrokerPosition> positions, CancellationToken ct)
    {
        var instrumentByConid = new Dictionary<long, BrokerageInstrument>();

        foreach (var position in positions)
        {
            if (position.Conid is not long conid || instrumentByConid.ContainsKey(conid))
                continue;

            var instrument = await _instrumentRepository.GetByConidAsync(userId, Provider, conid, ct);
            if (instrument is null)
            {
                instrument = new BrokerageInstrument(
                    userId, Provider, conid, position.Symbol, position.InstrumentType, position.Isin);
                await _instrumentRepository.AddAsync(instrument, ct);
            }
            else
            {
                instrument.RefreshIdentity(position.Symbol, position.InstrumentType, position.Isin);
                _instrumentRepository.Update(instrument);
            }

            instrumentByConid[conid] = instrument;
        }

        if (instrumentByConid.Count > 0)
            await _instrumentRepository.SaveChangesAsync(ct);

        return instrumentByConid;
    }
}
