namespace FinanceSentry.Modules.Research.Domain.Scoring;

/// <summary>
/// Why a symbol has no verdict to show (issue #1414): a closed set the page words, so a missing card is never
/// silent. Carried beside a null scorecard on the dossier and on a chip's batch row; null when a verdict exists.
/// </summary>
public static class ScorecardGapReason
{
    /// <summary>The caller lacks <c>ai.use</c>: verdicts are the owner's, by design.</summary>
    public const string NoRole = "no_role";

    /// <summary>The name is outside what the verdict scores (crypto, a fund type the rules do not read); reading again cannot help.</summary>
    public const string OutsideUniverse = "outside_universe";

    /// <summary>The scorecard read threw (a source failed, the reader broke): a retry may succeed.</summary>
    public const string Unreadable = "unreadable";
}
