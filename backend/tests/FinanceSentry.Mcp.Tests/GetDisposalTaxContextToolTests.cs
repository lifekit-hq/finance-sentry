using System.ComponentModel;
using System.Reflection;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Mcp.Tools;
using FinanceSentry.Modules.Research.Application.Queries;
using FinanceSentry.Modules.Research.Domain.Tax;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FinanceSentry.Mcp.Tests;

public sealed class GetDisposalTaxContextToolTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly Mock<IQueryHandler<GetDisposalTaxContextQuery, DisposalTaxContext?>> _handler = new();

    private GetDisposalTaxContextTool CreateSut(Guid? resolvedUserId = null) =>
        new(_handler.Object, new FakeIdentityResolver { ResolvedUserId = resolvedUserId }, NullLogger<GetDisposalTaxContextTool>.Instance);

    private static DisposalTaxContext Withheld() => new(
        "VUAA", DisposalTaxCalculator.Withheld, [DisposalTaxCalculator.Unclassified], null, null, false, "EUR",
        null, null, null, null, null, null, null, null, null, [], DisposalTaxCalculator.Method, string.Empty,
        DisposalTaxCalculator.Disclaimer);

    [Fact]
    public async Task ExecuteAsync_ReturnsTheContext_ForTheAuthenticatedUser()
    {
        _handler
            .Setup(h => h.Handle(
                It.Is<GetDisposalTaxContextQuery>(q => q.UserId == UserId && q.Symbol == "VUAA"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Withheld());

        var result = await CreateSut(UserId).ExecuteAsync("VUAA");

        result.Should().NotBeNull();
        result!.WithheldReasons.Should().Equal(DisposalTaxCalculator.Unclassified);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsNull_WhenThereIsNoIdentity()
    {
        var result = await CreateSut(resolvedUserId: null).ExecuteAsync("VUAA");

        result.Should().BeNull();
        _handler.Verify(
            h => h.Handle(It.IsAny<GetDisposalTaxContextQuery>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsNull_ForABlankSymbol()
    {
        (await CreateSut(UserId).ExecuteAsync("  ")).Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsNull_WhenTheHandlerThrows()
    {
        _handler
            .Setup(h => h.Handle(It.IsAny<GetDisposalTaxContextQuery>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db unavailable"));

        (await CreateSut(UserId).ExecuteAsync("VUAA")).Should().BeNull();
    }

    [Fact]
    public void Description_FramesTheFiguresAsContext_AndTellsTheAgentNotToEstimateWhenWithheld()
    {
        var description = typeof(GetDisposalTaxContextTool)
            .GetMethod(nameof(GetDisposalTaxContextTool.ExecuteAsync))!
            .GetCustomAttribute<DescriptionAttribute>()!
            .Description;

        description.Should().Contain("never a trading signal, tax advice or a tax return");
        description.Should().Contain("Do not estimate tax yourself for a withheld position");
    }
}
