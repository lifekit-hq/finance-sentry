namespace FinanceSentry.Core.Interfaces;

/// <summary>
/// Connection state of brokerage providers whose connection can lapse independently of their holdings (Inzhur's
/// cabinet session), so views that list holdings can say "reconnect needed" instead of only "stale".
/// </summary>
public interface IBrokerageConnectionStatusReader
{
    /// <summary>Provider → status (<c>active</c> / <c>reauth_required</c>) for the user's connections that report one.</summary>
    Task<IReadOnlyDictionary<string, string>> GetStatusesAsync(Guid userId, CancellationToken ct = default);
}
