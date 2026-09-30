namespace FinanceSentry.Modules.Companion.Tests;

using FinanceSentry.Core.Interfaces;

internal sealed class StubOwnerAccountReader(params Guid[] ownerIds) : IOwnerAccountReader
{
    public Task<bool> IsOwnerAsync(Guid userId, CancellationToken ct = default)
        => Task.FromResult(ownerIds.Contains(userId));
}
