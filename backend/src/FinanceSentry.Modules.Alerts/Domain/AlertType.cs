namespace FinanceSentry.Modules.Alerts.Domain;

public static class AlertType
{
    public const string LowBalance = "LowBalance";
    public const string SyncFailure = "SyncFailure";
    public const string UnusualSpend = "UnusualSpend";
    public const string ThesisBroken = "ThesisBroken";
    public const string MarketStructure = "MarketStructure";
    public const string PolicyViolation = "PolicyViolation";
    public const string Opportunity = "Opportunity";
    public const string ConsentExpiring = "ConsentExpiring";
    public const string JobFailure = "JobFailure";
    public const string PerformanceBrief = "PerformanceBrief";
    public const string CashShortfall = "CashShortfall";
}
