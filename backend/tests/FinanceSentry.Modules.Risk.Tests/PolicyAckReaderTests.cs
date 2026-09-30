namespace FinanceSentry.Modules.Risk.Tests;

using FinanceSentry.Modules.Risk.Application.Services;
using FinanceSentry.Modules.Risk.Domain;
using FinanceSentry.Modules.Risk.Domain.Repositories;
using FluentAssertions;
using Moq;
using Xunit;

public class PolicyAckReaderTests
{
    [Fact]
    public async Task ActiveAckOnRule_IsAcknowledged_OtherRuleIsNot()
    {
        var userId = Guid.NewGuid();
        var repo = new Mock<IPolicyViolationAckRepository>();
        repo.Setup(r => r.ListActiveAsync(userId, default)).ReturnsAsync(
            [new PolicyViolationAck { UserId = userId, RuleKey = RiskRuleKeys.MinCashBuffer, Subject = "CASH" }]);
        var reader = new PolicyAckReader(repo.Object);

        (await reader.IsPolicyAcknowledgedAsync(userId, RiskRuleKeys.MinCashBuffer)).Should().BeTrue();
        (await reader.IsPolicyAcknowledgedAsync(userId, RiskRuleKeys.MaxPositionWeight)).Should().BeFalse();
    }
}
