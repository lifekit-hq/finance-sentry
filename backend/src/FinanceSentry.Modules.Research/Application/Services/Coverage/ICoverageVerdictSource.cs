namespace FinanceSentry.Modules.Research.Application.Services.Coverage;

/// <summary>One current verdict reading: the word and the verdict rule that fired.</summary>
public sealed record CoverageVerdictReading(string Verdict, string FiredRuleId);

/// <summary>
/// The current verdict word of a ticker, the one the owner sees on the scorecard. A port so the coverage rules and
/// their tests do not depend on the market-data providers the scorecard reads.
/// </summary>
public interface ICoverageVerdictSource
{
    /// <summary>The ticker's verdict now, or null when the scorecard could not be read (the reading is skipped, not guessed).</summary>
    Task<CoverageVerdictReading?> ReadAsync(string ticker, CancellationToken ct = default);
}
