namespace FinanceSentry.Modules.Risk.Domain;

using FinanceSentry.Core.Auth;

/// <summary>
/// Point-in-time (symbol, quantity, usdValue) capture written once per position per
/// <see cref="Infrastructure.Jobs.RiskCheckJob"/> run. Own history — no other module persists
/// holdings-quantity history, and turnover/add-to-broken-thesis detection needs it.
/// </summary>
public class HoldingSnapshot : IUserOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    public string Symbol { get; set; } = string.Empty;

    public string Sleeve { get; set; } = string.Empty;

    public decimal Quantity { get; set; }

    public decimal UsdValue { get; set; }

    public DateTimeOffset CapturedAt { get; set; } = DateTimeOffset.UtcNow;
}

public static class RiskSleeve
{
    public const string Brokerage = "brokerage";
    public const string Crypto = "crypto";
    public const string Banking = "banking";
}
