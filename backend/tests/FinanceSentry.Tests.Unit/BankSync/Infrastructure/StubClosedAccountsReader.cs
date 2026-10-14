namespace FinanceSentry.Tests.Unit.BankSync.Infrastructure;

using FinanceSentry.Core.Interfaces;

internal sealed class StubClosedAccountsReader(params Guid[] closed) : IClosedAccountsReader
{
    public Task<IReadOnlySet<Guid>> GetClosedUserIdsAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlySet<Guid>>(closed.ToHashSet());
}
