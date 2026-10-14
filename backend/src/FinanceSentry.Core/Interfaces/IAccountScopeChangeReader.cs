namespace FinanceSentry.Core.Interfaces;

/// <summary>
/// Accounts or provider connections that entered or left a person's net-worth scope since a date. A net-worth change
/// over a window that contains one of these is mostly the book growing or shrinking, not the money moving, so the
/// dashboard states the amount without a percentage. Each module that owns linkable sources contributes one reader.
/// </summary>
public interface IAccountScopeChangeReader
{
    Task<IReadOnlyList<AccountScopeChange>> GetChangesSinceAsync(Guid userId, DateTime sinceUtc, CancellationToken ct = default);
}

/// <summary><paramref name="Kind"/> is <see cref="AccountScopeChangeKinds.Linked"/> or <see cref="AccountScopeChangeKinds.Removed"/>.</summary>
public record AccountScopeChange(string Source, string Kind, DateTime AtUtc);

public static class AccountScopeChangeKinds
{
    public const string Linked = "linked";
    public const string Removed = "removed";
}
