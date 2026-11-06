using System.Reflection;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Measures;
using FinanceSentry.Mcp.Auditing;
using FinanceSentry.Mcp.Tools;
using FinanceSentry.Modules.Wealth.Application.Queries;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FinanceSentry.Mcp.Tests;

public sealed class GetFinancialProfileToolTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly Mock<IQueryHandler<GetFinancialProfileQuery, FinancialProfileResponse>> _handler = new();

    private GetFinancialProfileTool CreateSut(Guid? resolvedUserId = null) =>
        new(_handler.Object, new FakeIdentityResolver { ResolvedUserId = resolvedUserId }, NullLogger<GetFinancialProfileTool>.Instance);

    private static MethodInfo Method => typeof(GetFinancialProfileTool).GetMethod(nameof(GetFinancialProfileTool.ExecuteAsync))!;

    internal static FinancialProfileResponse Profile() => new(
        DateTimeOffset.UtcNow,
        new ProfileSection<CashflowSummary>(DataQuality.Missing, ["INSUFFICIENT_MONTHS"], null),
        new ProfileSection<GoalsSummary>(DataQuality.Missing, ["NO_GOALS"], null),
        new ProfileSection<IncomeSummary>(DataQuality.Missing, ["NO_DECLARED_INCOME"], null),
        new ProfileSection<CareerSummary>(DataQuality.Missing, ["NO_CAREER_EVENTS"], null),
        new ProfileSection<DisposalTaxSummary>(DataQuality.Missing, ["NO_POSITIONS"], null),
        new ProfileSection<IpsRiskProfile>(DataQuality.Missing, ["NO_IPS"], null),
        new CapacitySuggestion(CapacityStatus.Insufficient, null, null, false, [], [], CapacityIndicator.RunwayUnknown),
        ["cashflow", "goals", "income", "career", "disposalTax", "risk"]);

    [Fact]
    public async Task ExecuteAsync_ReturnsTheProfile_ForTheAuthenticatedUser()
    {
        var profile = Profile();
        _handler
            .Setup(h => h.Handle(It.Is<GetFinancialProfileQuery>(q => q.UserId == UserId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);

        (await CreateSut(UserId).ExecuteAsync()).Should().BeSameAs(profile);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsNull_WhenThereIsNoIdentity()
    {
        (await CreateSut(resolvedUserId: null).ExecuteAsync()).Should().BeNull();
        _handler.Verify(h => h.Handle(It.IsAny<GetFinancialProfileQuery>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsNull_WhenTheHandlerThrows()
    {
        _handler
            .Setup(h => h.Handle(It.IsAny<GetFinancialProfileQuery>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db unavailable"));

        (await CreateSut(UserId).ExecuteAsync()).Should().BeNull();
    }

    [Fact]
    public void TheTool_IsMarkedSensitiveProfile_SoEveryCallIsAudited()
    {
        var marker = Method.GetCustomAttribute<SensitiveProfileAttribute>();

        marker.Should().NotBeNull("the profile aggregates declared income, goals and career events");
        marker!.FieldNames.Should().Contain(["Amount", "Cadence", "Kind", "Date"]);
        SensitiveProfileReads.CatalogOf(typeof(GetFinancialProfileTool).Assembly).Should().ContainKey("get_financial_profile");
    }
}
