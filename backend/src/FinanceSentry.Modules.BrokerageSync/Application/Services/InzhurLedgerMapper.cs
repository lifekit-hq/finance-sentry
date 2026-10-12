using System.Globalization;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.Inzhur;

namespace FinanceSentry.Modules.BrokerageSync.Application.Services;

/// <summary>A ledger row that moved certificates: a purchase, a sale or a gift. Money in hryvnia, quantity signed.</summary>
public sealed record InzhurLedgerTrade(
    string Id, string Type, string Symbol, string? Isin, DateTime TradeDateTime, decimal Quantity, decimal Price, decimal Proceeds);

/// <summary>A ledger row that moved money only: deposits, withdrawals, distributions, taxes, commissions, bonuses.</summary>
public sealed record InzhurLedgerCash(
    string Id, string Type, string? Code, string? Symbol, string? Isin, DateTime DateTime, decimal Amount);

public sealed record InzhurLedger(
    IReadOnlyList<InzhurLedgerTrade> Trades, IReadOnlyList<InzhurLedgerCash> Cash, IReadOnlyList<string> SkippedTypes);

/// <summary>
/// Maps the cabinet's ledger (<c>api/v1/users/transactions</c>) onto the shapes BrokerageSync already stores for IBKR
/// (money-semantics §1, "Inzhur"). The cabinet sends every amount unsigned and in kopecks; the sign is the one its own
/// transaction list shows for the row's type, and <see cref="InzhurHoldingsMapper.FromMinorUnits"/> converts the unit.
/// Rows whose type the cabinet did not know when this was written are skipped and their type named, never guessed.
/// </summary>
public static class InzhurLedgerMapper
{
    public const string Purchase = "Purchase";
    public const string PurchaseForBonuses = "Purchase for bonuses";
    public const string Sale = "Sale";

    /// <summary>Certificates given away (the cabinet signs it "-" and shows no amount).</summary>
    public const string GiftAgreement = "Gift Agreement";

    private const int IsinMaxLength = 12;

    // The cabinet's transactionTypes table: + money in, - money out.
    private static readonly Dictionary<string, int> CashSigns = new(StringComparer.Ordinal)
    {
        ["Deposit"] = 1,
        ["Dividend crediting"] = 1,
        ["Bonuses crediting"] = 1,
        ["Distribution crediting"] = 1,
        ["Conversion In"] = 1,
        ["Dividend payment"] = -1,
        ["Withdrawal"] = -1,
        ["Bonus withdrawal"] = -1,
        ["Dividend tax"] = -1,
        ["Bonuses tax"] = -1,
        ["Sale tax"] = -1,
        ["Sale commission"] = -1,
        ["Distribution tax"] = -1,
        ["Distribution payment"] = -1,
        ["Conversion Out"] = -1,
        ["Conversion Surcharge"] = -1,
    };

    // Certificates in (+) or out (-) for the rows that carry a contract.
    private static readonly Dictionary<string, int> TradeSigns = new(StringComparer.Ordinal)
    {
        [Purchase] = 1,
        [PurchaseForBonuses] = 1,
        [Sale] = -1,
        [GiftAgreement] = -1,
    };

    public static InzhurLedger Map(IEnumerable<InzhurTransaction> transactions)
    {
        var trades = new List<InzhurLedgerTrade>();
        var cash = new List<InzhurLedgerCash>();
        var skipped = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var row in transactions)
        {
            var id = row.IdText;
            var type = row.Type?.Trim() ?? string.Empty;
            if (id.Length == 0 || ParseDate(row.ExternalCreatedAt) is not { } at)
            {
                skipped.Add(type.Length == 0 ? "(no type)" : type);
                continue;
            }

            var amount = InzhurHoldingsMapper.FromMinorUnits(row.Amount);
            var symbol = SymbolFor(row.Asset);
            var isin = IsinFor(row.Asset);

            if (TradeSigns.TryGetValue(type, out var unitSign) && row.Contract?.Quantity is > 0m && symbol is not null)
            {
                var quantity = row.Contract.Quantity.Value;
                // A gift carries no proceeds; a purchase pays the amount out, a sale brings it in (IBKR's sign convention).
                var proceeds = type == GiftAgreement ? 0m : -unitSign * amount;
                trades.Add(new InzhurLedgerTrade(id, type, symbol, isin, at, unitSign * quantity, amount / quantity, proceeds));
            }
            else if (TradeSigns.TryGetValue(type, out var moneySign) || CashSigns.TryGetValue(type, out moneySign))
            {
                // A purchase or sale without a readable contract keeps its money movement; its units stay untracked.
                var signed = TradeSigns.ContainsKey(type) ? -moneySign * amount : moneySign * amount;
                cash.Add(new InzhurLedgerCash(id, type, Blank(row.DistributionType), symbol, isin, at, signed));
            }
            else
            {
                skipped.Add(type.Length == 0 ? "(no type)" : type);
            }
        }

        return new InzhurLedger(trades, cash, skipped.ToList());
    }

    private static string? SymbolFor(InzhurTransactionAsset? asset)
    {
        if (asset is null)
            return null;
        var name = InzhurHoldingsMapper.FirstNonBlank(asset.Name);
        var isin = InzhurHoldingsMapper.FirstNonBlank(asset.Isin);
        return name is null && isin is null ? null : InzhurHoldingsMapper.SymbolFor(asset.Type, name, isin, id: null);
    }

    private static string? IsinFor(InzhurTransactionAsset? asset)
    {
        var isin = InzhurHoldingsMapper.FirstNonBlank(asset?.Isin);
        return isin is null || isin.Length > IsinMaxLength ? null : isin;
    }

    private static DateTime? ParseDate(string? value)
        => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed.UtcDateTime
            : null;

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
