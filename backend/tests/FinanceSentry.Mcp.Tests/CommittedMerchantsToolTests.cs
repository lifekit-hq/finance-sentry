using FinanceSentry.Core.Cqrs;
using FinanceSentry.Mcp.Tools;
using FinanceSentry.Modules.BankSync.Application.Commands;
using FinanceSentry.Modules.BankSync.Application.Queries;
using FinanceSentry.Modules.BankSync.Domain.Exceptions;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinanceSentry.Mcp.Tests;

/// <summary>
/// Contract for the <c>committed_merchants</c> tool (spec 554, rule (d)) — the agent-facing way
/// to declare an obligation no detector or category rule can see.
/// </summary>
public sealed class CommittedMerchantsToolTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly Mock<IQueryHandler<ListCommittedMerchantPinsQuery, IReadOnlyList<CommittedMerchantPinDto>>>
        _list = new();

    private readonly Mock<ICommandHandler<PinCommittedMerchantCommand, PinCommittedMerchantResult>> _pin = new();
    private readonly Mock<ICommandHandler<UnpinCommittedMerchantCommand, bool>> _unpin = new();

    private CommittedMerchantsTool CreateSut(Guid? resolvedIdentity = null) =>
        new(_list.Object, _pin.Object, _unpin.Object,
            new FakeIdentityResolver { ResolvedUserId = resolvedIdentity });

    private static CommittedMerchantPinDto Dto(string key, string display) =>
        new(Guid.NewGuid(), key, display, DateTime.UtcNow);

    // ── list ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_List_ReturnsThePins()
    {
        _list.Setup(h => h.Handle(
                It.Is<ListCommittedMerchantPinsQuery>(q => q.UserId == UserId),
                It.IsAny<CancellationToken>()))
             .ReturnsAsync([Dto("anytime fitness", "Anytime Fitness")]);

        var result = await CreateSut().ExecuteAsync("list", userId: UserId);

        result!.Action.Should().Be("list");
        result.Pins.Should().ContainSingle().Which.DisplayName.Should().Be("Anytime Fitness");
        result.Error.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_ActionIsCaseAndPaddingTolerant()
    {
        _list.Setup(h => h.Handle(It.IsAny<ListCommittedMerchantPinsQuery>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync([]);

        var result = await CreateSut().ExecuteAsync("  LIST ", userId: UserId);

        result!.Action.Should().Be("list");
        result.Pins.Should().BeEmpty();
    }

    // ── pin / unpin ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Pin_PassesTheMerchantThrough_AndReportsANewPin()
    {
        var dto = Dto("mario scalas", "Mario Scalas");
        _pin.Setup(h => h.Handle(
                It.Is<PinCommittedMerchantCommand>(c => c.UserId == UserId && c.Merchant == "Mario Scalas"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PinCommittedMerchantResult(dto, AlreadyPinned: false));

        var result = await CreateSut().ExecuteAsync("pin", "Mario Scalas", UserId);

        result!.Action.Should().Be("pin");
        result.Pin!.MerchantKey.Should().Be("mario scalas");
        result.AlreadyPinned.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_Pin_ReportsAnAlreadyHeldPinRatherThanFailing()
    {
        _pin.Setup(h => h.Handle(It.IsAny<PinCommittedMerchantCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PinCommittedMerchantResult(Dto("netflix", "Netflix"), AlreadyPinned: true));

        var result = await CreateSut().ExecuteAsync("pin", "NETFLIX.COM", UserId);

        result!.AlreadyPinned.Should().BeTrue();
        result.Error.Should().BeNull();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExecuteAsync_Unpin_ReportsWhetherAnythingWasRemoved(bool removed)
    {
        _unpin.Setup(h => h.Handle(
                It.Is<UnpinCommittedMerchantCommand>(c => c.Merchant == "Netflix"),
                It.IsAny<CancellationToken>()))
              .ReturnsAsync(removed);

        var result = await CreateSut().ExecuteAsync("unpin", "Netflix", UserId);

        result!.Action.Should().Be("unpin");
        result.Unpinned.Should().Be(removed);
    }

    // ── malformed calls ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("pin")]
    [InlineData("unpin")]
    public async Task ExecuteAsync_MutationWithoutAMerchant_IsReportedNotAttempted(string action)
    {
        var result = await CreateSut().ExecuteAsync(action, merchant: "  ", userId: UserId);

        result!.Error.Should().Contain("requires a merchant");
        _pin.VerifyNoOtherCalls();
        _unpin.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ExecuteAsync_UnknownAction_IsReportedWithTheAllowedSet()
    {
        var result = await CreateSut().ExecuteAsync("delete", "Netflix", UserId);

        result!.Error.Should().Contain("list, pin, unpin");
        _list.VerifyNoOtherCalls();
        _pin.VerifyNoOtherCalls();
        _unpin.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ExecuteAsync_UnnameableMerchant_IsReportedAsAResultNotThrown()
    {
        // An MCP caller can act on a message; it cannot read a transport-level exception.
        _pin.Setup(h => h.Handle(It.IsAny<PinCommittedMerchantCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UnpinnableMerchantException());

        var result = await CreateSut().ExecuteAsync("pin", "***", UserId);

        result!.Error.Should().Contain("recognisable merchant name");
    }

    // ── identity ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_FallsBackToTheAuthenticatedIdentity_WhenNoUserIdIsPassed()
    {
        _list.Setup(h => h.Handle(
                It.Is<ListCommittedMerchantPinsQuery>(q => q.UserId == UserId),
                It.IsAny<CancellationToken>()))
             .ReturnsAsync([]);

        var result = await CreateSut(resolvedIdentity: UserId).ExecuteAsync("list");

        result!.Action.Should().Be("list");
        _list.Verify(h => h.Handle(
            It.Is<ListCommittedMerchantPinsQuery>(q => q.UserId == UserId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_NoIdentityAtAll_TouchesNothing()
    {
        var result = await CreateSut().ExecuteAsync("pin", "Mario Scalas");

        result.Should().BeNull();
        _pin.VerifyNoOtherCalls();
    }
}
