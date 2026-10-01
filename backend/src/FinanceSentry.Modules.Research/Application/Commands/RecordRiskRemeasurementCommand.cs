namespace FinanceSentry.Modules.Research.Application.Commands;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Queries;
using FinanceSentry.Modules.Research.Domain.Repositories;

/// <summary>
/// Records a re-measurement of the owner's risk tolerance, capacity and drawdown tolerance (#700).
/// Tolerance and capacity are the 1-5 scales the statement already uses; the drawdown tolerance is
/// the largest peak-to-trough decline the owner says they would sit through, in percent.
/// </summary>
public record RecordRiskRemeasurementCommand(
    Guid UserId,
    int RiskTolerance,
    int RiskCapacity,
    decimal MaxDrawdownTolerancePct) : ICommand<RiskRemeasurementDto>;

public sealed class IpsValidationException(string message) : Exception(message);

/// <summary>
/// Writes the re-measurement as a NEW statement version through the same path as any other save -
/// the prior version is demoted, never edited - carrying every other field forward, then reports
/// the diff against the version it supersedes. The new version's <c>RiskMeasuredAt</c> is stamped
/// even when the owner re-confirms the same numbers: that a measurement happened is itself the record.
/// </summary>
public sealed class RecordRiskRemeasurementCommandHandler(
    IIpsRepository repo,
    ICommandHandler<SaveIpsCommand, IpsDto> saveIps)
    : ICommandHandler<RecordRiskRemeasurementCommand, RiskRemeasurementDto>
{
    public const int MinScale = 1;
    public const int MaxScale = 5;
    public const decimal MaxDrawdownPct = 100m;

    public async Task<RiskRemeasurementDto> Handle(RecordRiskRemeasurementCommand cmd, CancellationToken ct)
    {
        Validate(cmd);

        var previous = await repo.GetCurrentAsync(cmd.UserId, ct)
            ?? throw new IpsValidationException(
                "There is no policy statement on file to re-measure; author one with save_ips first.");

        var saved = await saveIps.Handle(
            new SaveIpsCommand(
                cmd.UserId,
                previous.Goals,
                previous.PrimaryHorizonYears,
                previous.EmergencyCushionUsd,
                cmd.RiskTolerance,
                cmd.RiskCapacity,
                cmd.MaxDrawdownTolerancePct,
                previous.AllocationTargets,
                previous.RebalancingRule,
                previous.ContributionPlan,
                previous.SellDiscipline,
                previous.CoolingOffDays,
                previous.Exclusions,
                previous.ReviewCadence,
                RiskRemeasured: true),
            ct);

        var current = await repo.GetCurrentAsync(cmd.UserId, ct) ?? throw new InvalidOperationException(
            "The re-measured policy statement version was saved but is not readable.");

        return new RiskRemeasurementDto(saved, GetIpsDiffQueryHandler.ToDto(previous, current));
    }

    private static void Validate(RecordRiskRemeasurementCommand cmd)
    {
        if (cmd.RiskTolerance is < MinScale or > MaxScale)
            throw new IpsValidationException($"{nameof(cmd.RiskTolerance)} must be between {MinScale} and {MaxScale}.");

        if (cmd.RiskCapacity is < MinScale or > MaxScale)
            throw new IpsValidationException($"{nameof(cmd.RiskCapacity)} must be between {MinScale} and {MaxScale}.");

        if (cmd.MaxDrawdownTolerancePct is <= 0m or > MaxDrawdownPct)
            throw new IpsValidationException(
                $"{nameof(cmd.MaxDrawdownTolerancePct)} must be a percent above 0 and at most {MaxDrawdownPct:0}.");
    }
}
