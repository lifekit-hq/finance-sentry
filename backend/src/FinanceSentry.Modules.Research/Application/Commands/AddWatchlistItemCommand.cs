namespace FinanceSentry.Modules.Research.Application.Commands;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Exceptions;
using FinanceSentry.Modules.Research.Domain.Repositories;
using Microsoft.Extensions.Logging;

public record AddWatchlistItemCommand(
    Guid UserId,
    string Ticker,
    string? Exchange,
    string? Note,
    ThesisEventOrigin Origin = ThesisEventOrigin.User) : ICommand<WatchlistItemDto>;

public class AddWatchlistItemCommandHandler(
    IWatchlistRepository repo,
    IThesisEventRecorder eventRecorder,
    IResearchWriteScope writeScope,
    ILogger<AddWatchlistItemCommandHandler> logger)
    : ICommandHandler<AddWatchlistItemCommand, WatchlistItemDto>
{
    public async Task<WatchlistItemDto> Handle(AddWatchlistItemCommand cmd, CancellationToken ct)
    {
        var ticker = cmd.Ticker.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(ticker))
        {
            throw new WatchlistItemNotFoundException();
        }

        var existing = await repo.FindAsync(cmd.UserId, ticker, ct);
        if (existing is not null)
        {
            throw new WatchlistItemAlreadyExistsException();
        }

        var item = new WatchlistItem
        {
            UserId = cmd.UserId,
            Ticker = ticker,
            Exchange = cmd.Exchange?.Trim(),
            Note = cmd.Note?.Trim(),
        };

        await writeScope.RunAsync(cmd.Origin, async () =>
        {
            await repo.AddAsync(item, ct);
            await WatchlistJournal.RecordAsync(
                eventRecorder, logger, cmd.UserId, item.Id, ticker, ThesisEventType.Created, cmd.Origin, item.Note, ct);
            return item;
        }, ct);
        return new WatchlistItemDto(item.Id, item.Ticker, item.Exchange, item.Note, item.AddedAt, cmd.Origin.ToString());
    }
}

/// <summary>
/// The journal entry of a watchlist change. Ledger's change must leave a trace (the origin shown on reads is read
/// from it), so for Ledger a failed append surfaces and rolls the change back (see <see cref="IResearchWriteScope"/>); the owner's own change stays best-effort, as a thesis save does.
/// </summary>
internal static class WatchlistJournal
{
    public static async Task RecordAsync(
        IThesisEventRecorder recorder,
        ILogger logger,
        Guid userId,
        Guid itemId,
        string ticker,
        ThesisEventType eventType,
        ThesisEventOrigin origin,
        string? note,
        CancellationToken ct)
    {
        try
        {
            await recorder.RecordAuditedAsync(
                userId, ThesisSubjectType.Watchlist, itemId, ticker, eventType, origin,
                new ThesisAudit(Prior: null, New: null, note, Evidence: null), note, ct);
        }
        catch (Exception ex) when (origin == ThesisEventOrigin.User)
        {
            logger.LogError(
                ex, "{EventType} event recording failed for watchlist entry {ItemId} ({Ticker})", eventType, itemId, ticker);
        }
    }
}
