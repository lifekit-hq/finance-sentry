namespace FinanceSentry.Modules.Research.Application.Commands;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Repositories;

public record SaveIpsCommand(
    Guid UserId,
    IReadOnlyList<InvestmentGoal> Goals,
    int PrimaryHorizonYears,
    decimal? EmergencyCushionUsd,
    int RiskTolerance,
    int? RiskCapacity,
    decimal? MaxDrawdownTolerancePct,
    IReadOnlyList<AllocationTarget> AllocationTargets,
    RebalancingRule? RebalancingRule,
    ContributionPlan? ContributionPlan,
    string? SellDiscipline,
    int? CoolingOffDays,
    IReadOnlyList<string> Exclusions,
    string? ReviewCadence,
    bool RiskRemeasured = false) : ICommand<IpsDto>;

public class SaveIpsCommandHandler(IIpsRepository repo, TimeProvider timeProvider) : ICommandHandler<SaveIpsCommand, IpsDto>
{
    private const string DefaultReviewCadence = "annual";

    public async Task<IpsDto> Handle(SaveIpsCommand cmd, CancellationToken ct)
    {
        var nextVersion = await repo.GetMaxVersionAsync(cmd.UserId, ct) + 1;
        var previous = await repo.GetCurrentAsync(cmd.UserId, ct);
        var riskCapacity = cmd.RiskCapacity ?? previous?.RiskCapacity;
        var maxDrawdownTolerancePct = cmd.MaxDrawdownTolerancePct ?? previous?.MaxDrawdownTolerancePct;

        var ips = new InvestmentPolicyStatement
        {
            UserId = cmd.UserId,
            Version = nextVersion,
            IsCurrent = true,
            Goals = cmd.Goals.ToList(),
            PrimaryHorizonYears = cmd.PrimaryHorizonYears,
            EmergencyCushionUsd = cmd.EmergencyCushionUsd,
            RiskTolerance = cmd.RiskTolerance,
            RiskCapacity = riskCapacity,
            MaxDrawdownTolerancePct = maxDrawdownTolerancePct,
            RiskMeasuredAt = RiskMeasuredAtFor(cmd, previous, riskCapacity, maxDrawdownTolerancePct),
            AllocationTargets = cmd.AllocationTargets.ToList(),
            RebalancingRule = cmd.RebalancingRule ?? Domain.RebalancingRule.Default,
            ContributionPlan = cmd.ContributionPlan,
            SellDiscipline = cmd.SellDiscipline?.Trim(),
            Exclusions = cmd.Exclusions.ToList(),
            ReviewCadence = string.IsNullOrWhiteSpace(cmd.ReviewCadence)
                ? DefaultReviewCadence
                : cmd.ReviewCadence.Trim(),
        };

        if (cmd.CoolingOffDays is { } days)
        {
            ips.CoolingOffDays = days;
        }

        await repo.AddVersionAsync(ips, ct);

        return new IpsDto(
            ips.Id, ips.Version, ips.IsCurrent, ips.Goals, ips.PrimaryHorizonYears,
            ips.EmergencyCushionUsd, ips.RiskTolerance, ips.RiskCapacity, ips.MaxDrawdownTolerancePct, ips.RiskMeasuredAt,
            ips.AllocationTargets, ips.RebalancingRule, ips.ContributionPlan, ips.SellDiscipline,
            ips.CoolingOffDays, ips.Exclusions, ips.ReviewCadence,
            ips.LastReviewedAt, ips.CreatedAt, ips.UpdatedAt);
    }

    /// <summary>
    /// A save that omits capacity or drawdown tolerance keeps the recorded values (an amendment for
    /// another reason must not silently drop an enforced threshold). It stamps a fresh measurement
    /// when it is the first statement, says so explicitly, or changed tolerance, capacity or drawdown
    /// tolerance; otherwise the prior measurement carries forward untouched, so a version written for
    /// another reason never passes for a re-measurement.
    /// </summary>
    private DateTimeOffset? RiskMeasuredAtFor(
        SaveIpsCommand cmd, InvestmentPolicyStatement? previous, int? riskCapacity, decimal? maxDrawdownTolerancePct)
    {
        var riskChanged = previous is null
            || previous.RiskTolerance != cmd.RiskTolerance
            || previous.RiskCapacity != riskCapacity
            || previous.MaxDrawdownTolerancePct != maxDrawdownTolerancePct;

        return cmd.RiskRemeasured || riskChanged ? timeProvider.GetUtcNow() : previous?.RiskMeasuredAt;
    }
}
