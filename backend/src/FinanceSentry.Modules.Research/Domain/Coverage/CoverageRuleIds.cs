namespace FinanceSentry.Modules.Research.Domain.Coverage;

/// <summary>Stable ids of the coverage add and remove rules, recorded on every journal row and shown by <c>explain_coverage</c>.</summary>
public static class CoverageRuleIds
{
    public const string Held = "held";
    public const string Watchlist = "watchlist";
    public const string LedgerNomination = "ledger_nomination";
    public const string BuyTwice = "buy_twice_in_window";

    public const string AvoidTwice = "avoid_twice";
    public const string CandidateExpired = "candidate_expired";
    public const string NoEvent = "no_event";
}
