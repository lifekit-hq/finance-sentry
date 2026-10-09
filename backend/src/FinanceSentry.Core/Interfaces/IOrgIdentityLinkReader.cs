namespace FinanceSentry.Core.Interfaces;

/// <summary>
/// Whether a local account is linked to the org identity provider (Logto), the login that proves a real person
/// is behind the account. Lets a module ask without reaching into the Auth module's Identity tables.
/// </summary>
public interface IOrgIdentityLinkReader
{
    Task<bool> IsLinkedAsync(Guid userId, CancellationToken ct = default);
}
