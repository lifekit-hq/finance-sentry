namespace FinanceSentry.Modules.Research.API.Responses;

/// <summary>
/// The Ledger scoreboard, "an arena of one" (#986, rallies comparison option B1): contestants graded over the same
/// windows against the S&amp;P 500, which is the zero line - every figure is excess return over it in percentage points,
/// from the decision (or pick) to <see cref="HorizonDays"/> later. A decided proposal is one observation however its
/// window overlaps another's, so the sample counts decisions, not days. Expired and superseded proposals are listed in
/// <see cref="EndedWithoutDecision"/> and never scored.
/// </summary>
/// <param name="Benchmark">The zero line's name: "S&amp;P 500".</param>
/// <param name="BenchmarkTicker">The ticker it is priced through: "SPY" (a price return, like the name's).</param>
/// <param name="HorizonDays">The scoring window after each decision or pick.</param>
/// <param name="MinimumSampleSize">Scored rows a contestant needs before its figures can tell skill from luck.</param>
/// <param name="Contestants">
/// <c>LedgerAsProposed</c> (every decided proposal as if followed and executed, signed by its direction),
/// <c>OwnerAsDecided</c> (the owner's decision value, the one the Decisions page shows), then one <c>PaperPicks</c>
/// row per candidate source (User, Ledger, Scan).
/// </param>
/// <param name="EndedWithoutDecision">Proposals that expired or were superseded before a decision, newest first.</param>
/// <param name="Coverage">
/// The coverage universe ("our own index") graded against the S&amp;P 500 from each member's add date: does the list
/// beat the index. Null only when the read has no coverage or price source wired.
/// </param>
public sealed record LedgerScoreboardDto(
    string Benchmark,
    string BenchmarkTicker,
    int HorizonDays,
    int MinimumSampleSize,
    IReadOnlyList<ScoreboardContestantDto> Contestants,
    IReadOnlyList<UndecidedProposalDto> EndedWithoutDecision,
    CoverageVsBenchmarkDto? Coverage = null);

/// <summary>One contestant's row on the scoreboard.</summary>
/// <param name="Contestant"><c>LedgerAsProposed</c>, <c>OwnerAsDecided</c> or <c>PaperPicks</c>.</param>
/// <param name="Source">For <c>PaperPicks</c>, the candidate source (User, Ledger, Scan); null otherwise.</param>
/// <param name="Decided">Proposals confirmed or rejected (rejected ones count for both contestants); for paper picks, picks recorded.</param>
/// <param name="Executed">Confirmed proposals a matching fill of the owner's own followed; null for paper picks, which never trade.</param>
/// <param name="Scored">Rows graded at the horizon - the sample size <c>n</c>.</param>
/// <param name="AwaitingHorizon">Rows still inside their window (or waiting for their execution to settle).</param>
/// <param name="NotScored">
/// Rows that will never get a grade: for the owner, a confirmed order never placed or a rejected Hold (no direction);
/// for any contestant, a row with no price stamp at the decision or the horizon.
/// </param>
/// <param name="WindowStart">The first decision (or pick) in this contestant's rows; null when there is none.</param>
/// <param name="Gross">Figures before costs.</param>
/// <param name="Net">Figures after spec 020's friction (<c>ThesisTrackRecord:Friction</c>): per-trade cost, then tax on a gain.</param>
/// <param name="TooFewToTell">True while <see cref="Scored"/> is below the minimum sample size.</param>
/// <param name="Caveat">"too few to tell skill from luck (n of 30)" while <see cref="TooFewToTell"/>; null after.</param>
public sealed record ScoreboardContestantDto(
    string Contestant,
    string? Source,
    string Label,
    int Decided,
    int? Executed,
    int Scored,
    int AwaitingHorizon,
    int NotScored,
    DateTimeOffset? WindowStart,
    ScoreboardFiguresDto Gross,
    ScoreboardFiguresDto Net,
    bool TooFewToTell,
    string? Caveat);

/// <summary>
/// Excess-return figures over the scored rows, in percentage points against the S&amp;P 500; null with no scored row.
/// <see cref="BeatBenchmarkSharePct"/> is the share (0-100) that beat the S&amp;P 500 - never a win rate against zero.
/// </summary>
public sealed record ScoreboardFiguresDto(
    decimal? MeanExcessPct,
    decimal? MedianExcessPct,
    decimal? BeatBenchmarkSharePct,
    ScoreboardRowDto? Worst);

/// <summary>One graded row: a decided proposal (<see cref="Kind"/> is its action) or a paper pick (<c>Pick</c>).</summary>
public sealed record ScoreboardRowDto(
    Guid Id,
    string Ticker,
    string Kind,
    DateTimeOffset From,
    DateTimeOffset To,
    decimal ExcessPct);

/// <summary>A proposal that closed without the owner's decision: listed, never scored.</summary>
public sealed record UndecidedProposalDto(
    Guid Id,
    string Ticker,
    string Action,
    string Status,
    DateTimeOffset CreatedAt);

/// <summary>
/// The coverage universe against the S&amp;P 500, deterministic from stored daily bars (no model call). Each membership
/// (an add, to its removal or today) is one equal-weight observation graded from its add date over each horizon; the
/// list return is the mean of those returns and the benchmark return the mean of the S&amp;P 500's over the same
/// windows, so <c>ExcessPct</c> is the list's average edge in percentage points. A horizon with no member old enough
/// reads "not enough history yet" and carries no figures.
/// </summary>
/// <param name="Benchmark">"S&amp;P 500".</param>
/// <param name="BenchmarkTicker">"SPY".</param>
/// <param name="HeadlineHorizonDays">The horizon the answer is read at (90).</param>
/// <param name="MinimumSampleSize">Graded memberships a figure needs before it can tell skill from luck.</param>
/// <param name="CurrentMembers">Members covered now.</param>
/// <param name="Horizons">One block per horizon, shortest first; the headline one is flagged.</param>
public sealed record CoverageVsBenchmarkDto(
    string Benchmark,
    string BenchmarkTicker,
    int HeadlineHorizonDays,
    int MinimumSampleSize,
    int CurrentMembers,
    IReadOnlyList<CoverageHorizonDto> Horizons);

/// <summary>One horizon: the whole list, then each source (add reason and screen tag) on its own.</summary>
public sealed record CoverageHorizonDto(
    int HorizonDays,
    bool IsHeadline,
    CoverageGradeDto Overall,
    IReadOnlyList<CoverageSourceGradeDto> BySource);

/// <summary>Grades of the memberships one reason (and screen tag) nominated.</summary>
/// <param name="Source">The <c>CoverageReason</c>: Held, Watchlist, Screen, Ledger or User.</param>
/// <param name="ScreenTag">The nominating screen's first nomination reason where the candidate carries one; null otherwise.</param>
public sealed record CoverageSourceGradeDto(string Source, string? ScreenTag, CoverageGradeDto Grade);

/// <summary>Equal-weight figures over memberships at one horizon; percentages are points, figures null without a graded membership.</summary>
/// <param name="Memberships">Memberships considered (each add is one; a re-added name counts again).</param>
/// <param name="Graded">Memberships with a return over the window (full horizon, or to removal).</param>
/// <param name="ThroughRemoval">Of those, memberships that ended before the horizon: graded to the removal date, kept as history.</param>
/// <param name="Awaiting">Memberships whose horizon has not elapsed, or whose closing bar has not landed.</param>
/// <param name="NotPriced">Memberships with no stored bar at entry or exit, so no return.</param>
/// <param name="Status"><c>Graded</c> or <c>NotEnoughHistory</c>.</param>
/// <param name="Message">"not enough history yet" while <c>NotEnoughHistory</c>; null otherwise.</param>
public sealed record CoverageGradeDto(
    int Memberships,
    int Graded,
    int ThroughRemoval,
    int Awaiting,
    int NotPriced,
    decimal? ListReturnPct,
    decimal? BenchmarkReturnPct,
    decimal? ExcessPct,
    decimal? BeatBenchmarkSharePct,
    string Status,
    string? Message,
    bool TooFewToTell,
    string? Caveat);
