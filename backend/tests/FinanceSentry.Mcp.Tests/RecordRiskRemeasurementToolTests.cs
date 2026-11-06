using System.Reflection;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Measures;
using FinanceSentry.Mcp.Auditing;
using FinanceSentry.Mcp.Tools;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Commands;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Wealth.Application.Queries;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FinanceSentry.Mcp.Tests;

/// <summary>#454 S7: the capacity indicator is shown beside the recorded capacity and never changes what is recorded.</summary>
public sealed class RecordRiskRemeasurementToolTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly Mock<ICommandHandler<RecordRiskRemeasurementCommand, RiskRemeasurementDto>> _record = new();
    private readonly Mock<IQueryHandler<GetFinancialProfileQuery, FinancialProfileResponse>> _profile = new();
    private readonly RiskRemeasurementDto _recorded = new(
        new IpsDto(
            Guid.NewGuid(), Version: 5, IsCurrent: true, [], PrimaryHorizonYears: 20, EmergencyCushionUsd: null,
            RiskTolerance: 3, RiskCapacity: 2, MaxDrawdownTolerancePct: 20m, RiskMeasuredAt: DateTimeOffset.UtcNow,
            AllocationTargets: [], RebalancingRule.Default, ContributionPlan: null, SellDiscipline: null, CoolingOffDays: 90,
            Exclusions: [], ReviewCadence: "quarterly", LastReviewedAt: null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
        new IpsDiffDto(4, DateTimeOffset.UtcNow.AddDays(-90), 5, DateTimeOffset.UtcNow, []));

    private RecordRiskRemeasurementTool CreateSut(Guid? resolvedUserId = null) => new(
        _record.Object, _profile.Object, new FakeIdentityResolver { ResolvedUserId = resolvedUserId },
        NullLogger<RecordRiskRemeasurementTool>.Instance);

    private void Suggests(int? band) => _profile
        .Setup(p => p.Handle(It.Is<GetFinancialProfileQuery>(q => q.UserId == UserId), It.IsAny<CancellationToken>()))
        .ReturnsAsync(GetFinancialProfileToolTests.Profile() with
        {
            CapacityIndicator = new CapacitySuggestion(
                band is null ? CapacityStatus.Insufficient : CapacityStatus.Suggested, band, 2, true, [], [], null),
        });

    private void RecordSucceeds() => _record
        .Setup(h => h.Handle(It.IsAny<RecordRiskRemeasurementCommand>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(_recorded);

    [Fact]
    public async Task ExecuteAsync_RecordsTheNumbersTheOwnerGave_AndShowsTheSuggestionBesideThem()
    {
        Suggests(4);
        RecordSucceeds();

        var result = await CreateSut(UserId).ExecuteAsync(riskTolerance: 3, riskCapacity: 2, maxDrawdownTolerancePct: 20m);

        _record.Verify(
            h => h.Handle(new RecordRiskRemeasurementCommand(UserId, 3, 2, 20m), It.IsAny<CancellationToken>()), Times.Once);
        result!.Ips.Should().BeSameAs(_recorded.Ips);
        result.Diff.Should().BeSameAs(_recorded.Diff);
        result.CapacityIndicator!.SuggestedBand.Should().Be(4);
        result.CapacityIndicator.RecordedCapacity.Should().Be(2, "the number just recorded, whatever the IPS held before");
        result.CapacityIndicator.DiffersFromRecorded.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_AnOwnerWhoRecordsTheSuggestedBand_DoesNotDiffer()
    {
        Suggests(4);
        RecordSucceeds();

        var result = await CreateSut(UserId).ExecuteAsync(3, riskCapacity: 4, 20m);

        result!.CapacityIndicator!.DiffersFromRecorded.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_WhenTheIndicatorSuggestsNothing_NothingDiffers()
    {
        Suggests(null);
        RecordSucceeds();

        var result = await CreateSut(UserId).ExecuteAsync(3, 4, 20m);

        result!.CapacityIndicator!.SuggestedBand.Should().BeNull();
        result.CapacityIndicator.DiffersFromRecorded.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_WhenTheProfileCannotBeRead_StillRecords_WithoutAnIndicator()
    {
        _profile
            .Setup(p => p.Handle(It.IsAny<GetFinancialProfileQuery>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db"));
        RecordSucceeds();

        var result = await CreateSut(UserId).ExecuteAsync(3, 4, 20m);

        result.Should().NotBeNull();
        result!.CapacityIndicator.Should().BeNull();
        _record.Verify(h => h.Handle(It.IsAny<RecordRiskRemeasurementCommand>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsNull_AndRecordsNothing_WhenThereIsNoIdentity()
    {
        (await CreateSut(resolvedUserId: null).ExecuteAsync(3, 4, 20m)).Should().BeNull();
        _record.Verify(h => h.Handle(It.IsAny<RecordRiskRemeasurementCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_AValidationFailure_StillSurfaces_AndTheIndicatorIsNotWrittenAnywhere()
    {
        Suggests(4);
        _record
            .Setup(h => h.Handle(It.IsAny<RecordRiskRemeasurementCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IpsValidationException("RiskCapacity must be between 1 and 5."));

        var act = () => CreateSut(UserId).ExecuteAsync(3, 9, 20m);

        await act.Should().ThrowAsync<IpsValidationException>();
    }

    [Fact]
    public void TheTool_IsAudited_BecauseTheIndicatorReadsDeclaredProfileData()
    {
        typeof(RecordRiskRemeasurementTool).GetMethod(nameof(RecordRiskRemeasurementTool.ExecuteAsync))!
            .GetCustomAttribute<SensitiveProfileAttribute>().Should().NotBeNull();
    }
}
