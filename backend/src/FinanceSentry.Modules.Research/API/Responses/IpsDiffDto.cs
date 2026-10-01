namespace FinanceSentry.Modules.Research.API.Responses;

using FinanceSentry.Modules.Research.Domain;

/// <summary>What changed between two versions of the policy statement (#700); values are JSON, <c>null</c> = unset.</summary>
public sealed record IpsDiffDto(
    int FromVersion,
    DateTimeOffset FromCreatedAt,
    int ToVersion,
    DateTimeOffset ToCreatedAt,
    IReadOnlyList<IpsFieldChange> Changes);

/// <summary>A recorded risk re-measurement: the new statement version and exactly what it changed.</summary>
public sealed record RiskRemeasurementDto(IpsDto Ips, IpsDiffDto Diff);
