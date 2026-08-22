namespace FinanceSentry.Modules.Companion.Domain;

/// <summary>
/// The source class of a captured material event (feature 031). Most map 1:1 from an alert type; the
/// alerts module already decided these are worth surfacing. <see cref="AnalystAction"/> is the one
/// non-alert source (a street action on a held name).
/// </summary>
public enum CompanionEventKind
{
    RiskViolation,
    SyncFailure,
    UnusualSpend,
    Opportunity,
    ThesisBreak,
    MarketStructure,
    LowBalance,
    CashShortfall,
    AnalystAction,
    ConsentExpiring,
    OperationalFailure,
}
