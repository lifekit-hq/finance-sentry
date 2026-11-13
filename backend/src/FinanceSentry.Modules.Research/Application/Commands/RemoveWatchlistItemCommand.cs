namespace FinanceSentry.Modules.Research.Application.Commands;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Repositories;
using Microsoft.Extensions.Logging;

public record RemoveWatchlistItemCommand(
    Guid UserId,
    Guid ItemId,
    ThesisEventOrigin Origin = ThesisEventOrigin.User) : ICommand<bool>;

public class RemoveWatchlistItemCommandHandler(
    IWatchlistRepository repo,
    IThesisEventRecorder eventRecorder,
    ILogger<RemoveWatchlistItemCommandHandler> logger)
    : ICommandHandler<RemoveWatchlistItemCommand, bool>
{
    public async Task<bool> Handle(RemoveWatchlistItemCommand cmd, CancellationToken ct)
    {
        var item = await repo.FindByIdAsync(cmd.UserId, cmd.ItemId, ct);
        if (item is null || !await repo.RemoveAsync(cmd.UserId, cmd.ItemId, ct))
        {
            return false;
        }

        await WatchlistJournal.RecordAsync(
            eventRecorder, logger, cmd.UserId, item.Id, item.Ticker, ThesisEventType.Closed, cmd.Origin, item.Note, ct);
        return true;
    }
}
