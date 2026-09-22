namespace FinanceSentry.Modules.Retention.Application;

using FinanceSentry.Modules.Retention.Domain;

/// <summary>
/// The single, versioned source of truth for every persistent table's retention decision
/// (feature 024, FR-001). Reviewable in code, not database config. The coverage-guard test asserts
/// no mapped table is missing a policy, so a new feature that adds a table without a decision fails CI.
///
/// Windows here are defaults; <see cref="RetentionOptions.WindowOverrides"/> retunes any table at runtime.
/// FR-008 is honored: only derived/observational data is purged/downsampled; user-initiated financial
/// records (transactions, accounts, theses, budgets, holdings, IPS) are Keep.
/// </summary>
public static class RetentionPolicyRegistry
{
    private const string BankSync = "bank_sync";
    private const string Research = "research";
    private const string Radar = "radar";
    private const string Risk = "risk";
    private const string Alerts = "alerts";
    private const string Analytics = "analytics";
    private const string Companion = "companion";
    private const string CryptoSync = "crypto_sync";
    private const string BrokerageSync = "brokerage_sync";
    private const string Budgets = "budgets";
    private const string Auth = "auth";
    private const string Public = "public";
    private const string Retention = "retention";
    private const string Agent = "agent";
    private const string Events = "events";

    /// <summary>
    /// Every <c>DbContext</c> the retention policy set must account for. The coverage-guard test
    /// reflects over the loaded module assemblies and asserts it finds exactly this set — so adding a
    /// new module/context forces its author to record retention decisions and update this list.
    /// </summary>
    public static readonly IReadOnlySet<string> KnownContexts = new HashSet<string>
    {
        "AuthDbContext", "BankSyncDbContext", "CryptoSyncDbContext", "BrokerageSyncDbContext",
        "AlertsDbContext", "BudgetsDbContext", "SubscriptionsDbContext", "WealthDbContext",
        "ResearchDbContext", "RadarDbContext", "RiskDbContext", "CompanionDbContext",
        "AnalyticsDbContext", "RetentionDbContext", "AgentDbContext", "EventsDbContext",
    };

    /// <summary>All retention decisions — exactly one per persistent table.</summary>
    public static readonly IReadOnlyList<RetentionPolicy> All =
    [
        // ── Generic purge: derived/observational rows the engine deletes on a schedule ───────────
        Purge(BankSync, "audit_logs", "PerformedAt", 365, "Data-access audit trail; observational."),
        Purge(BankSync, "SyncJobs", "CreatedAt", 90, "Per-sync run history; grows fast, low long-term value."),
        Purge(Analytics, "query_audit", "CreatedAt", 180, "Analytics query audit (feature 033)."),
        Purge(Companion, "companion_events", "CapturedAt", 90, "Dispatched outbox rows; watermark in companion_capture_state survives."),
        Purge(Research, "candidate_scores", "ScoredAt", 180, "Opportunity scoring history."),
        Purge(Research, "valuation_snapshots", "CapturedAt", 365, "Point-in-time valuation observations."),
        Purge(Research, "macro_events", "EventDate", 365, "Past macro-calendar entries."),
        Purge(Research, "recommendation_trends", "IngestedAt", 365, "Monthly consensus; stale once a ticker stops being covered."),
        Purge(Risk, "holding_snapshots", "CapturedAt", 180, "Risk holding time-series."),
        // Events (049): the reader's verdict on a fired event outlives the 90d companion row it points at.
        Purge(Events, "event_verdicts", "RecordedAt", 365, "Reader verdicts on fired events; the alert they explain purges at 90d."),
        // The retention module governs its own run-record tables (one row per run; drill rows aren't
        // artifact-pruned). 180d keeps a recent Verified backup_runs row for the SC-002 query.
        Purge(Retention, "retention_runs", "StartedAt", 180, "Retention/downsample run history."),
        Purge(Retention, "backup_runs", "CreatedAt", 180, "Backup + restore-drill history (artifact prune handles live backups)."),

        // ── Downsample (US3, P2 — job gated off by default) ──────────────────────────────────────
        Downsample(Radar, "daily_bars", "Date", 365, "Daily OHLC → weekly beyond a year."),
        Downsample(Public, "net_worth_snapshots", "SnapshotDate", 365, "Daily net-worth → weekly beyond a year."),

        // ── Bespoke: already governed by a pre-existing module job (engine skips; listed for FR-001) ─
        Bespoke(Alerts, "alerts", "CreatedAt", 90, "AlertPurgeJob", "Resolved/dismissed alerts purged monthly."),
        Bespoke(Radar, "radar_signals", "Timestamp", 730, "RadarComputeJob", "Info-severity signals pruned during compute."),
        Bespoke(Research, "opportunity_candidates", "ExpiresAt", null, "CandidateExpiryJob", "Expired candidates removed daily."),
        // Transactions are soft-archived (IsActive=false) at 24m by DataRetentionJob, never hard-deleted.
        Keep(BankSync, "Transactions", "User financial history; soft-archived at 24m by DataRetentionJob, never deleted."),

        // ── Keep forever: user-initiated records + reference/config + current-state snapshots ─────
        Keep(BankSync, "BankAccounts", "User accounts."),
        Keep(BankSync, "MonobankCredentials", "Provider credentials."),
        Keep(BankSync, "TrueLayerConnections", "Provider connections."),
        Keep(BankSync, "categories", "Reference taxonomy."),
        Keep(BankSync, "mcc_categories", "Reference taxonomy."),
        Keep(BankSync, "merchant_keywords", "Reference bridge table."),

        Keep(CryptoSync, "CryptoHoldings", "Current-state holdings."),
        Keep(CryptoSync, "ExchangeCredentials", "Provider credentials."),
        Keep(BrokerageSync, "BrokerageHoldings", "Current-state holdings."),
        Keep(BrokerageSync, "IBKRCredentials", "Provider credentials."),

        Keep(Budgets, "budgets", "User budgets."),
        Keep(Public, "detected_subscriptions", "Derived but small + user-facing; keep by decision."),

        Keep(Companion, "companion_capture_state", "Capture watermark; tiny."),
        Keep(Companion, "companion_notification_settings", "User settings."),

        Keep(Radar, "radar_universe_members", "Universe config."),

        Keep(Research, "theses", "User investment theses."),
        Keep(Research, "thesis_events", "Thesis track record; growing but user value — revisit window later."),
        Keep(Research, "watchlist_items", "User watchlist."),
        Keep(Research, "investment_policy_statements", "User IPS."),
        Keep(Research, "analyst_actions", "Companion data; growing — revisit window later."),
        Keep(Research, "analyst_universe_members", "Universe config."),
        Keep(Research, "news_articles", "Feeds RAG corpus; growing — revisit once provenance is decoupled."),
        Keep(Research, "news_sources", "Source config."),
        Keep(Research, "quote_cache", "Quote cache; growing — revisit window later."),
        Keep(Research, "research_documents", "RAG corpus."),
        Keep(Research, "research_chunks", "RAG corpus."),
        Keep(Research, "research_embeddings", "RAG corpus."),

        Keep(Risk, "risk_rule_sets", "User risk rules."),
        Keep(Risk, "policy_violation_acks", "User acknowledgements."),

        // Agent (040): browser Ledger chat threads — user-facing financial-context conversations.
        Keep(Agent, "agent_conversations", "User agent conversations; user-facing, keep by decision."),
        Keep(Agent, "agent_messages", "Agent conversation messages; cascade with their conversation."),

        // Auth: identity + tokens. Small; managed by the auth flow. Keep by decision.
        Keep(Auth, "AspNetUsers", "Identity."),
        Keep(Auth, "AspNetRoles", "Identity."),
        Keep(Auth, "AspNetRoleClaims", "Identity."),
        Keep(Auth, "AspNetUserClaims", "Identity."),
        Keep(Auth, "AspNetUserLogins", "Identity."),
        Keep(Auth, "AspNetUserRoles", "Identity."),
        Keep(Auth, "AspNetUserTokens", "Identity."),
        Keep(Auth, "RefreshTokens", "Auth tokens; rotated by the auth flow."),
        Keep(Auth, "McpAuthorizationCodes", "Short-lived OAuth codes; consumed by the auth flow."),
        Keep(Auth, "McpServiceTokens", "Service tokens."),
    ];

    /// <summary>Generic-engine purge policies the <c>RetentionPurgeService</c> actively runs.</summary>
    public static IEnumerable<RetentionPolicy> GenericPurgePolicies => All.Where(p => p.IsGenericPurge);

    /// <summary>Downsample policies (US3).</summary>
    public static IEnumerable<RetentionPolicy> DownsamplePolicies =>
        All.Where(p => p.Action == RetentionAction.Downsample);

    private static RetentionPolicy Purge(string schema, string table, string ts, int windowDays, string notes)
        => new(schema, table, ts, RetentionAction.Purge, windowDays, Notes: notes);

    private static RetentionPolicy Downsample(string schema, string table, string ts, int windowDays, string notes)
        => new(schema, table, ts, RetentionAction.Downsample, windowDays, Notes: notes);

    private static RetentionPolicy Bespoke(string schema, string table, string? ts, int? windowDays, string job, string notes)
        => new(schema, table, ts, RetentionAction.Purge, windowDays, RetentionEnforcer.Bespoke, job, Notes: notes);

    private static RetentionPolicy Keep(string schema, string table, string notes)
        => new(schema, table, null, RetentionAction.Keep, null, Notes: notes);
}
