namespace FinanceSentry.Core.Interfaces;

/// <summary>Answers whether a user holds the Owner role, for background work that has no request principal.</summary>
public interface IOwnerAccountReader
{
    Task<bool> IsOwnerAsync(Guid userId, CancellationToken ct = default);
}
