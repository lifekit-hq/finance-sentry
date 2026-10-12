namespace FinanceSentry.Modules.Research.Domain;

using FinanceSentry.Core.Auth;

public class WatchlistItem : IUserOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Ticker { get; set; } = string.Empty;
    public string? Exchange { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;
}
