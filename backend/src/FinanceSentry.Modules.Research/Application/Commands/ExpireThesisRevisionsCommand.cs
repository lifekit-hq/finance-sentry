namespace FinanceSentry.Modules.Research.Application.Commands;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.Domain.Repositories;
using FinanceSentry.Modules.Research.Domain.Revisions;

/// <summary>Expires every pending thesis revision past its expiry, across users. Expired rows stay.</summary>
public sealed record ExpireThesisRevisionsCommand(DateTimeOffset AsOf) : ICommand<int>;

public sealed class ExpireThesisRevisionsCommandHandler(IThesisRevisionRepository repo)
    : ICommandHandler<ExpireThesisRevisionsCommand, int>
{
    public async Task<int> Handle(ExpireThesisRevisionsCommand command, CancellationToken ct)
    {
        var expired = await repo.ListExpiredUnscopedAsync(command.AsOf, ct);
        foreach (var revision in expired)
        {
            revision.Status = ThesisRevisionStatus.Expired;
            await repo.UpdateAsync(revision, ct);
        }

        return expired.Count;
    }
}
