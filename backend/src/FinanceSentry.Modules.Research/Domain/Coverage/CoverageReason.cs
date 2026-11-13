namespace FinanceSentry.Modules.Research.Domain.Coverage;

/// <summary>Why a ticker entered the coverage universe (<see cref="CoverageMember.AddedReason"/>).</summary>
public enum CoverageReason
{
    /// <summary>The user holds the ticker.</summary>
    Held,

    /// <summary>The ticker is on the user's watchlist.</summary>
    Watchlist,

    /// <summary>A machine-scan (<c>Scan</c>) candidate scored Buy or StrongBuy twice inside the window.</summary>
    Screen,

    /// <summary>A Ledger candidate scored Buy or StrongBuy twice, or Ledger nominated the name with a decision note.</summary>
    Ledger,

    /// <summary>Reserved for a name the owner adds to coverage directly; no write path sets it yet.</summary>
    User,
}

/// <summary>What a <see cref="CoverageJournalEntry"/> records.</summary>
public enum CoverageAction
{
    Added,
    Removed,
}
