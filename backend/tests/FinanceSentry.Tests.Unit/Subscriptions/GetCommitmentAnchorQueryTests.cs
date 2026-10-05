namespace FinanceSentry.Tests.Unit.Subscriptions;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Subscriptions.Application.Queries;
using FinanceSentry.Modules.Subscriptions.Domain.Exceptions;
using FluentAssertions;
using Moq;
using Xunit;

public class GetCommitmentAnchorQueryTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid TransactionId = Guid.NewGuid();

    private static GetCommitmentAnchorQueryHandler Handler(CommitmentTransaction? transaction)
    {
        var reader = new Mock<ICommitmentTransactionReader>();
        reader.Setup(r => r.FindAsync(UserId, TransactionId, It.IsAny<CancellationToken>())).ReturnsAsync(transaction);
        return new GetCommitmentAnchorQueryHandler(reader.Object);
    }

    [Fact]
    public async Task Handle_ReturnsTheLatestSameKeyCharge()
    {
        var latest = new CommitmentTransaction(
            "netflix", "Netflix", 12.99m, "EUR", new DateOnly(2026, 9, 5), 3, SubscriptionCadences.Monthly);

        var result = await Handler(latest).Handle(new GetCommitmentAnchorQuery(UserId, TransactionId), default);

        result.Amount.Should().Be(12.99m);
        result.Currency.Should().Be("EUR");
        result.Date.Should().Be(new DateOnly(2026, 9, 5));
        result.ChargeCount.Should().Be(3);
        result.Cadence.Should().Be(SubscriptionCadences.Monthly);
    }

    [Fact]
    public async Task Handle_UnknownTransaction_Throws404()
    {
        var act = () => Handler(null).Handle(new GetCommitmentAnchorQuery(UserId, TransactionId), default);

        var ex = await act.Should().ThrowAsync<CommitmentTransactionNotFoundException>();
        ex.Which.StatusCode.Should().Be(404);
    }
}
