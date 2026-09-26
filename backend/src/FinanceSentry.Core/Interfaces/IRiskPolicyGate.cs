namespace FinanceSentry.Core.Interfaces;

/// <summary>
/// Core-facing promotion-time policy check — lets other modules (Research's candidate promotion,
/// 019) run the 022 risk gate before creating a position-sized thesis, without depending on the
/// Risk module directly.
/// </summary>
public interface IRiskPolicyGate
{
    Task<RiskGateVerdict> CheckProposalAsync(
        Guid userId, string ticker, decimal proposedUsd, bool overrideFlag, bool isPaper = false, CancellationToken ct = default);
}

public enum RiskGateDecision
{
    Allowed,
    Refused,
}

/// <summary>Projection of Risk's <c>RiskVerdict</c> across the module boundary.</summary>
public sealed record RiskGateVerdict(
    RiskGateDecision Decision,
    string? RuleKey,
    decimal? ObservedValue,
    decimal? LimitValue,
    decimal? MaxCompliantSizeUsd,
    string? Note = null);
