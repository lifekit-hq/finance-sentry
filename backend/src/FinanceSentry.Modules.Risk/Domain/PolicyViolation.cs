namespace FinanceSentry.Modules.Risk.Domain;

public enum PolicyViolationStatus
{
    New,
    Acknowledged,
    Worsened,
}

/// <summary>A deterministic fact: a rule was breached by an observed value.</summary>
public sealed record PolicyViolation(
    string RuleKey,
    string Subject,
    decimal ObservedValue,
    decimal LimitValue,
    decimal ExcessUsd,
    decimal ExcessPct,
    PolicyViolationStatus Status,
    string? RemediationNote = null,
    decimal? WorseningStepPct = null)
{
    /// <summary>
    /// True for a fresh violation or one that has worsened past its acknowledged step; false for an
    /// acknowledged violation that has not (yet) crossed that step. A caller — human or agent — can
    /// trust this to decide whether to report without re-deriving acknowledgement state itself (#689).
    /// </summary>
    public bool Reportable => Status != PolicyViolationStatus.Acknowledged;

    /// <summary>Whether an acknowledgement with a worsening step is on file for this violation.</summary>
    public bool IsAcknowledged => Status is PolicyViolationStatus.Acknowledged or PolicyViolationStatus.Worsened;

    /// <summary>Whether the observed value has crossed the acknowledged worsening step.</summary>
    public bool HasWorsenedPastStep => Status == PolicyViolationStatus.Worsened;
}

public static class RiskRuleKeys
{
    public const string MaxPositionWeight = "MaxPositionWeight";
    public const string MaxSleeveWeight = "MaxSleeveWeight";
    public const string MinCashBuffer = "MinCashBuffer";
    public const string MaxNewPosition = "MaxNewPosition";
    public const string Turnover = "Turnover";
    public const string AllocationDrift = "AllocationDrift";
    public const string AddToBrokenThesis = "AddToBrokenThesis";
}
