namespace FinanceSentry.Modules.Research.Application.Commands;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.Domain.Repositories;

public record DeleteThesisCommand(Guid UserId, Guid Id) : ICommand<bool>;

/// <summary>
/// Deleting a thesis retires any news source registered to it (thesis-source staleness, orphaned half)
/// before the delete runs: <c>news_sources.ThesisId</c> is <c>ON DELETE SET NULL</c>, so once the thesis
/// row is gone the FK itself erases the only link back to it — this has to happen first, or a source
/// silently reverts to looking like an ordinary market-wide default source with no record of why it
/// stopped meaning anything. See <see cref="Infrastructure.Jobs.ThesisSourceRetirementJob"/> for the
/// other half (thesis text edited so it no longer matches).
/// </summary>
public class DeleteThesisCommandHandler(IThesisRepository repo, INewsSourceRepository sources)
    : ICommandHandler<DeleteThesisCommand, bool>
{
    public async Task<bool> Handle(DeleteThesisCommand cmd, CancellationToken ct)
    {
        var thesis = await repo.FindAsync(cmd.UserId, cmd.Id, ct);
        if (thesis is null)
        {
            return false;
        }

        var linked = await sources.ListByThesisAsync(cmd.Id, ct);
        foreach (var source in linked)
        {
            if (source.RetiredReason is not null)
            {
                continue;
            }

            source.Enabled = false;
            source.RetiredAt = DateTimeOffset.UtcNow;
            source.RetiredReason = $"Owning thesis {thesis.Ticker} was deleted";
            await sources.UpdateAsync(source, ct);
        }

        return await repo.DeleteAsync(cmd.UserId, cmd.Id, ct);
    }
}
