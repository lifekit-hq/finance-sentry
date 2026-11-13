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

    /// <summary>
    /// Something deterministic happened to a thesis's name (a filing, a passed earnings or catalyst date, a news cluster, a
    /// break or an unbreak): Ledger should re-read the thesis. Its own kind, never folded into <see cref="NewsCluster"/> or
    /// <see cref="FilingLanded"/>, so batching those can never swallow it. Filing, break and unbreak are realtime; news,
    /// earnings and catalyst ride the digest lane.
    /// </summary>
    ThesisReviewDue,

    /// <summary>The same for an Active candidate with no thesis (a watchlist name); same lanes.</summary>
    CandidateReviewDue,
}
