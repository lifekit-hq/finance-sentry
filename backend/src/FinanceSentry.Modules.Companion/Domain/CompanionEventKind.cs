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
    PriceHike,
    DuplicateCharge,
    CategorySpike,
    FxSpread,
    PerformanceBrief,
    RebalanceProposal,
    CashSweepProposal,
    EarningsAhead,
    FilingLanded,
    NewsCluster,
    BudgetBreach,
    FamilyStatement,
    FireBrief,
    PolicyReview,
    PolicyReviewMissed,
    RelativeUnderperformance,
    MacroEventAhead,
    MacroEventReleased,

    /// <summary>
    /// A connection, or one account, the connection-health policy drives has gone Dormant: the one notice that it may be
    /// closed. Digest material - one line, never a wake.
    /// </summary>
    ConnectionDormant,

    /// <summary>A bank account was retired (absent from its provider's list, or replaced by a reissue): one digest line, never a wake.</summary>
    AccountRetired,

    /// <summary>An account's day-to-day spend has nearly stopped while its syncs succeed: spend may be moving off-book.</summary>
    CoverageGap,
}
