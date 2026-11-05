using System.ComponentModel;
using System.Reflection;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Mcp.Auditing;
using FinanceSentry.Mcp.Tools;
using FinanceSentry.Modules.Wealth.Application.Queries;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FinanceSentry.Mcp.Tests;

public sealed class GetIncomeProfileToolTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly Mock<IQueryHandler<GetIncomeProfileQuery, IncomeProfileResponse>> _handler = new();

    private GetIncomeProfileTool CreateSut(Guid? resolvedUserId = null) =>
        new(_handler.Object, new FakeIdentityResolver { ResolvedUserId = resolvedUserId }, NullLogger<GetIncomeProfileTool>.Instance);

    private static MethodInfo Method => typeof(GetIncomeProfileTool).GetMethod(nameof(GetIncomeProfileTool.ExecuteAsync))!;

    [Fact]
    public async Task ExecuteAsync_ReturnsTheProfile_ForTheAuthenticatedUser()
    {
        var profile = new IncomeProfileResponse(true, [], 0, null, null, 3_700m, null, IncomeVariance.NoDeclared);
        _handler
            .Setup(h => h.Handle(It.Is<GetIncomeProfileQuery>(q => q.UserId == UserId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);

        var result = await CreateSut(UserId).ExecuteAsync();

        result.Should().BeSameAs(profile);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsNull_WhenThereIsNoIdentity()
    {
        (await CreateSut(resolvedUserId: null).ExecuteAsync()).Should().BeNull();
        _handler.Verify(h => h.Handle(It.IsAny<GetIncomeProfileQuery>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsNull_WhenTheHandlerThrows()
    {
        _handler
            .Setup(h => h.Handle(It.IsAny<GetIncomeProfileQuery>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db unavailable"));

        (await CreateSut(UserId).ExecuteAsync()).Should().BeNull();
    }

    [Fact]
    public void TheTool_IsMarkedSensitiveProfile_SoEveryCallIsAudited()
    {
        var marker = Method.GetCustomAttribute<SensitiveProfileAttribute>();

        marker.Should().NotBeNull("a tool that returns declared income must be on the audit trail");
        marker!.FieldNames.Should().Contain(["Label", "Amount"]);
    }

    [Fact]
    public void Description_SaysDeclaredNeverReplacesObserved_AndWhatAKeylessHostSees()
    {
        var description = Method.GetCustomAttribute<DescriptionAttribute>()!.Description;

        description.Should().Contain("never replaces the observed figure");
        description.Should().Contain("DeclaredUnreadable");
        description.Should().Contain("never advice");
    }
}
