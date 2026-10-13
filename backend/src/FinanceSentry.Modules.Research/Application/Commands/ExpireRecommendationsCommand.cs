namespace FinanceSentry.Modules.Research.Application.Commands;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain.Repositories;

/// <summary>Expires every pending proposal past its expiry, across users (#1069). Expired rows stay, journalled.</summary>
public sealed record ExpireRecommendationsCommand(DateTimeOffset AsOf) : ICommand<int>;

public sealed class ExpireRecommendationsCommandHandler(IRecommendationRepository repo, IThesisEventRecorder recorder)
    : ICommandHandler<ExpireRecommendationsCommand, int>
{
    public async Task<int> Handle(ExpireRecommendationsCommand command, CancellationToken ct)
    {
        var expired = await repo.ListExpiredUnscopedAsync(command.AsOf, ct);
        foreach (var recommendation in expired)
        {
            await RecommendationExpiry.ExpireAsync(repo, recorder, recommendation, ct);
        }

        return expired.Count;
    }
}
