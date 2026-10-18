namespace FinanceSentry.Core.Interfaces;

public interface IUserAssetVisibilityReader
{
    Task<UserAssetVisibility?> GetAsync(Guid userId, CancellationToken ct = default);
}

/// <summary>
/// A person's display rules for small and empty rows. <paramref name="SmallBalanceThreshold"/> is in the
/// person's base currency. Hiding is display-only: hidden rows still count in every total.
/// </summary>
public sealed record UserAssetVisibility(
    bool HideEmptyBalances,
    bool HideSmallBalances,
    decimal SmallBalanceThreshold);
