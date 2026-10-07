namespace FinanceSentry.Core.Utils;

using System.Globalization;
using System.Text.RegularExpressions;

/// <summary>
/// The one place an alert becomes a path inside the SPA: the page that shows the thing the alert is about, on the
/// routes and URL filters that already exist (<c>/transactions?account=&amp;category=&amp;type=&amp;from=&amp;to=</c>,
/// <c>/assets/:symbol</c>, the list pages). The Alerts page, web push and the companion event all read the result, so
/// they cannot drift apart. A relative path only — never a host, and never an amount or merchant name (spec 859 FR-003
/// keeps those off the lock screen, and the URL rides on the push payload).
/// </summary>
/// <remarks>
/// <see cref="Resolve"/> answers from what every stored alert carries (type, reference, label, creation time). Two types
/// need what only their emitter knows, so the emitter calls <see cref="ForPolicyViolation"/> /
/// <see cref="ForRelativeUnderperformance"/> and stores the answer: a policy violation's subject is a ticker, the book, a
/// sleeve or a quarter depending on the rule, and an underperformance label is a thesis ticker only at thesis scope.
/// A test pins every alert type to a row here so a new emitter cannot fall outside it silently.
/// </remarks>
public static partial class AlertAppPath
{
    public const string AccountsList = "/accounts/list";
    public const string AccountsInvestments = "/accounts/investments";
    public const string Dashboard = "/dashboard";
    public const string Transactions = "/transactions";
    public const string Subscriptions = "/subscriptions";

    /// <summary>The detection job pairs a conversion's legs inside this many days (<c>FxSpreadLookbackDays</c>), so the
    /// debit sits that far back from the alert at most.</summary>
    private const int FxSpreadWindowDays = 3;

    private const string IsoDate = "yyyy-MM-dd";
    private const string ThesisScope = "Thesis";

    /// <summary>Risk rules whose subject is a held symbol; the rest name the book, a sleeve, an asset class or a quarter.</summary>
    private static readonly HashSet<string> SymbolRules = new(StringComparer.Ordinal)
    {
        "MaxPositionWeight",
        "AddToBrokenThesis",
    };

    /// <summary>
    /// The path for an alert of <paramref name="type"/>, or null when nothing in the app shows what it is about (a job
    /// failure, an unknown type). <paramref name="createdAt"/> picks the month or day a spend alert is about.
    /// </summary>
    public static string? Resolve(string type, Guid? referenceId, string? referenceLabel, DateTimeOffset createdAt)
    {
        var label = referenceLabel?.Trim();
        var day = createdAt.UtcDateTime.Date;

        return type switch
        {
            // The account's own ledger; the alert's reference is the account.
            "LowBalance" or "CashShortfall" => referenceId is { } account
                ? Ledger(("account", account.ToString()))
                : AccountsList,

            // Reconnecting happens on the list, and there is no per-account page.
            "SyncFailure" or "ConsentExpiring" or "FamilyStatement" => AccountsList,

            "CategorySpike" => SpendIn(label, MonthStart(day), null),
            "BudgetBreach" => SpendIn(label, MonthStart(day), MonthEnd(day)),

            // No filtered view of one merchant's charges exists, and one conversion has no page of its own.
            "DuplicateCharge" or "UnusualSpend" => Transactions,
            "FxSpread" => Ledger(
                ("type", "debit"),
                ("from", day.AddDays(-FxSpreadWindowDays).ToString(IsoDate, CultureInfo.InvariantCulture)),
                ("to", day.ToString(IsoDate, CultureInfo.InvariantCulture))),

            "ThesisBroken" or "MarketStructure" or "Opportunity" or "EarningsAhead" or "FilingLanded" or "NewsCluster"
                => label is { } symbol && IsSymbol(symbol) ? Asset(symbol) : null,

            "PolicyViolation" or "RelativeUnderperformance" or "PerformanceBrief" or "RebalanceProposal"
                or "CashSweepProposal" or "PolicyReview" or "PolicyReviewMissed" => AccountsInvestments,

            "PriceHike" => Subscriptions,
            "FireBrief" => Dashboard,

            _ => null,
        };
    }

    /// <summary>The ledger narrowed to the transactions whose text contains <paramref name="query"/> (<c>?q=</c>), or null when blank.</summary>
    public static string? ForLedgerSearch(string? query)
        => query?.Trim() is { Length: > 0 } q ? Ledger(("q", q)) : null;

    /// <summary>A symbol's dossier. Null when <paramref name="symbol"/> is not shaped like one.</summary>
    public static string? ForSymbol(string? symbol)
        => symbol?.Trim() is { } trimmed && IsSymbol(trimmed) ? Asset(trimmed) : null;

    /// <summary>
    /// A policy violation opens the dossier when the rule is about one holding (an override always is: it is recorded
    /// against the ticker being traded), and the investments overview when it is about the book, a sleeve or a quarter.
    /// </summary>
    public static string ForPolicyViolation(string ruleKey, string subject, bool isOverride)
        => (isOverride || SymbolRules.Contains(ruleKey)) && ForSymbol(subject) is { } dossier
            ? dossier
            : AccountsInvestments;

    /// <summary>A thesis-scope underperformance opens that thesis's ticker; the book and a sleeve have no single holding.</summary>
    public static string ForRelativeUnderperformance(string scope, string label)
        => string.Equals(scope, ThesisScope, StringComparison.OrdinalIgnoreCase) && ForSymbol(label) is { } dossier
            ? dossier
            : AccountsInvestments;

    /// <summary>The month a budget breach happened in, for the category it is about: what was spent, not the budget.</summary>
    public static string ForBudgetBreach(string category, int year, int month)
    {
        var first = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
        return SpendIn(category.Trim(), MonthStart(first), MonthEnd(first));
    }

    private static string Asset(string symbol) => $"/assets/{Uri.EscapeDataString(symbol)}";

    private static bool IsSymbol(string value) => SymbolShape().IsMatch(value);

    private static string SpendIn(string? category, DateTime from, DateTime? to)
        => category is { Length: > 0 }
            ? Ledger(
                ("type", "debit"),
                ("category", category),
                ("from", from.ToString(IsoDate, CultureInfo.InvariantCulture)),
                ("to", to?.ToString(IsoDate, CultureInfo.InvariantCulture)))
            : Transactions;

    private static string Ledger(params (string Name, string? Value)[] filters)
        => Transactions + "?" + string.Join('&', filters
            .Where(f => f.Value is not null)
            .Select(f => $"{f.Name}={Uri.EscapeDataString(f.Value!)}"));

    private static DateTime MonthStart(DateTime day) => new(day.Year, day.Month, 1, 0, 0, 0, DateTimeKind.Utc);

    private static DateTime MonthEnd(DateTime day) => MonthStart(day).AddMonths(1).AddDays(-1);

    // A held ticker, ETF or crypto symbol: the radar's feed-freshness alert labels itself "freshness", which opens nothing.
    [GeneratedRegex("^[A-Z0-9][A-Z0-9.-]{0,14}$")]
    private static partial Regex SymbolShape();
}
