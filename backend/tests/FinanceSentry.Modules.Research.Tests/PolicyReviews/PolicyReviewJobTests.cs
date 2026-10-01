namespace FinanceSentry.Modules.Research.Tests.PolicyReviews;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.Application.Commands;
using FinanceSentry.Modules.Research.Domain.Repositories;
using FinanceSentry.Modules.Research.Infrastructure.Jobs;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

public class PolicyReviewJobTests
{
    private readonly Mock<IIpsRepository> _ipsRepo = new();
    private readonly Mock<ICommandHandler<RunPolicyReviewCommand, PolicyReviewRunResult>> _handler = new();

    [Fact]
    public async Task Every_user_with_a_policy_statement_is_checked()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        _ipsRepo.Setup(r => r.GetUserIdsWithCurrentIpsUnscopedAsync(It.IsAny<CancellationToken>())).ReturnsAsync([a, b]);
        _handler.Setup(h => h.Handle(It.IsAny<RunPolicyReviewCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PolicyReviewRunResult(PolicyReviewRunOutcome.NotDue, null, null, 0));

        await Job().ExecuteAsync();

        _handler.Verify(h => h.Handle(new RunPolicyReviewCommand(a), It.IsAny<CancellationToken>()), Times.Once);
        _handler.Verify(h => h.Handle(new RunPolicyReviewCommand(b), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task One_users_failure_does_not_block_the_rest()
    {
        var failing = Guid.NewGuid();
        var next = Guid.NewGuid();
        _ipsRepo.Setup(r => r.GetUserIdsWithCurrentIpsUnscopedAsync(It.IsAny<CancellationToken>())).ReturnsAsync([failing, next]);
        _handler.Setup(h => h.Handle(new RunPolicyReviewCommand(failing), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));
        _handler.Setup(h => h.Handle(new RunPolicyReviewCommand(next), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PolicyReviewRunResult(PolicyReviewRunOutcome.Completed, null, Guid.NewGuid(), 1));

        await Job().ExecuteAsync();

        _handler.Verify(h => h.Handle(new RunPolicyReviewCommand(next), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task No_policy_statements_means_no_reviews()
    {
        _ipsRepo.Setup(r => r.GetUserIdsWithCurrentIpsUnscopedAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);

        await Job().ExecuteAsync();

        _handler.Verify(h => h.Handle(It.IsAny<RunPolicyReviewCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private PolicyReviewJob Job() => new(_ipsRepo.Object, _handler.Object, NullLogger<PolicyReviewJob>.Instance);
}
