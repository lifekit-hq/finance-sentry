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
public sealed record LedgerScoreboardDto(
    string Benchmark,
    string BenchmarkTicker,
    int HorizonDays,
    int MinimumSampleSize,
    IReadOnlyList<ScoreboardContestantDto> Contestants,
    IReadOnlyList<UndecidedProposalDto> EndedWithoutDecision);

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
