namespace FinanceSentry.Modules.BrokerageSync.Application.Services;

using FinanceSentry.Core.Measures;

/// <summary>
/// Maps a broker-reported cash row onto the flow classes of finance-framework §3.2 for the brokerage scope, per
/// provider and by its own type string, never by sign alone. Deposits and withdrawals are external; dividends,
/// interest, fees and taxes are return (rule 2) - income paid out of the account is withdrawn, so it is an
/// external outflow. Trades and currency conversions move money inside the scope and are not flows. A type not
/// listed here is not classified (null): it is left out rather than guessed.
/// </summary>
public static class BrokerageFlowClassifier
{
    public const string Ibkr = "ibkr";
    public const string Inzhur = "inzhur";

    // Flex's CashTransaction types. The deposit row carries its direction in the amount's sign.
    private static readonly HashSet<string> IbkrExternal = new(StringComparer.OrdinalIgnoreCase)
    {
        "Deposits/Withdrawals",
        "Deposits & Withdrawals",
    };

    private static readonly HashSet<string> IbkrTax = new(StringComparer.OrdinalIgnoreCase)
    {
        "Withholding Tax",
        "871(m) Withholding",
    };

    // Return rows whose sign says whether they earned (income) or cost (a fee): interest received or paid, a
    // dividend or its reversal, a fee or its refund.
    private static readonly HashSet<string> IbkrReturn = new(StringComparer.OrdinalIgnoreCase)
    {
        "Dividends",
        "Payment In Lieu Of Dividends",
        "Broker Interest Received",
        "Broker Interest Paid",
        "Bond Interest Received",
        "Bond Interest Paid",
        "Other Fees",
        "Broker Fees",
        "Advisor Fees",
        "Commission Adjustments",
    };

    // Inzhur's ledger types (InzhurLedgerMapper.CashSigns); amounts are stored signed by that table.
    private static readonly Dictionary<string, FlowClass> InzhurClasses = new(StringComparer.Ordinal)
    {
        ["Deposit"] = FlowClass.ExternalInflow,
        ["Withdrawal"] = FlowClass.ExternalOutflow,
        ["Bonus withdrawal"] = FlowClass.ExternalOutflow,
        ["Dividend payment"] = FlowClass.ExternalOutflow,
        ["Distribution payment"] = FlowClass.ExternalOutflow,
        ["Dividend crediting"] = FlowClass.Income,
        ["Distribution crediting"] = FlowClass.Income,
        ["Bonuses crediting"] = FlowClass.Income,
        ["Dividend tax"] = FlowClass.Tax,
        ["Distribution tax"] = FlowClass.Tax,
        ["Bonuses tax"] = FlowClass.Tax,
        ["Sale tax"] = FlowClass.Tax,
        ["Sale commission"] = FlowClass.Fee,
        ["Conversion Surcharge"] = FlowClass.Fee,
    };

    /// <summary>The row's class for the brokerage scope, or null when its type is not a flow or not known.</summary>
    public static FlowClass? Classify(string provider, string transactionType, decimal amount)
    {
        if (amount == 0m)
            return null;

        if (string.Equals(provider, Inzhur, StringComparison.OrdinalIgnoreCase))
            return InzhurClasses.TryGetValue(transactionType, out var inzhur) ? inzhur : null;

        if (!string.Equals(provider, Ibkr, StringComparison.OrdinalIgnoreCase))
            return null;

        if (IbkrExternal.Contains(transactionType))
            return amount > 0m ? FlowClass.ExternalInflow : FlowClass.ExternalOutflow;
        if (IbkrTax.Contains(transactionType))
            return amount > 0m ? FlowClass.Income : FlowClass.Tax;

        return IbkrReturn.Contains(transactionType)
            ? amount > 0m ? FlowClass.Income : FlowClass.Fee
            : null;
    }

    /// <summary>Whether <paramref name="flowClass"/> crosses the scope's edge (a deposit or a withdrawal).</summary>
    public static bool IsExternal(FlowClass? flowClass)
        => flowClass is FlowClass.ExternalInflow or FlowClass.ExternalOutflow;
}
