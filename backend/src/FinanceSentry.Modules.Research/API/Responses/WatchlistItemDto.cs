namespace FinanceSentry.Modules.Research.API.Responses;

public record WatchlistItemDto(
    Guid Id,
    string Ticker,
    string? Exchange,
    string? Note,
    DateTimeOffset AddedAt,
    // Who added the entry: "User" or "Ledger".
    string Origin = "User");
