namespace FinanceSentry.Modules.Research.Application.Services;

/// <summary>
/// "Why is it moving" on the asset dossier (#1218): which daily returns count as a notable move and how far
/// around a move the dossier looks for news and Radar signals. Bound from configuration section
/// <c>NotableMoves</c>; every value has a documented default, so nothing needs to be configured.
/// </summary>
public sealed class NotableMoveOptions
{
    public const string SectionName = "NotableMoves";

    /// <summary>A day is a notable move when its close-to-close return is at least this many percent, up or down. Default 5.</summary>
    public decimal ThresholdPct { get; set; } = 5m;

    /// <summary>
    /// A news item or signal is linked to a move when it falls within this many days (UTC dates) before or after
    /// the move's day. Default 1.
    /// </summary>
    public int WindowDays { get; set; } = 1;

    /// <summary>
    /// The dossier's visible window: moves from the last this-many days are shown. Default 30. News and Radar
    /// signals are read back to <see cref="WindowDays"/> before the window's start, so a move on its first day still
    /// finds the items just before it.
    /// </summary>
    public int VisibleDays { get; set; } = 30;

    /// <summary>The most items listed under one move, best first. Default 5.</summary>
    public int MaxLinksPerMove { get; set; } = 5;
}
