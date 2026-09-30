namespace FinanceSentry.Modules.Risk.Tests;

using FinanceSentry.Modules.Risk.Application.Services;
using FinanceSentry.Modules.Risk.Domain;
using FinanceSentry.Modules.Risk.Domain.Ports;
using FinanceSentry.Modules.Risk.Domain.Repositories;
using FluentAssertions;
using Moq;
using Xunit;

public class PolicyAckReaderTests
{
    private const decimal MinCash = 0.10m;
    private const decimal CashPctAtAck = 0.08m;
    private const decimal WorseningStep = 0.02m;

    private readonly Guid _userId = Guid.NewGuid();
    private readonly Mock<IPolicyViolationAckRepository> _acks = new();
    private readonly Mock<IBookSnapshotReader> _book = new();
    private readonly Mock<IRiskRuleSetRepository> _ruleSets = new();
    private readonly Mock<IAllocationPolicySource> _allocations = new();

    private readonly Mock<IDrawdownCheckProvider> _drawdown = new();

    private PolicyAckReader Reader() => new(
        _acks.Object, _book.Object, _ruleSets.Object, _allocations.Object, _drawdown.Object, new RiskEvaluationService());

    private void GivenMinCashAck()
    {
        _acks.Setup(r => r.ListActiveAsync(_userId, default)).ReturnsAsync(
        [
            new PolicyViolationAck
            {
                UserId = _userId,
                RuleKey = RiskRuleKeys.MinCashBuffer,
                Subject = "CASH",
                ObservedAtAck = CashPctAtAck,
                WorseningStepPct = WorseningStep,
            },
        ]);
        _ruleSets.Setup(r => r.GetCurrentAsync(_userId, default))
            .ReturnsAsync(new RiskRuleSet { UserId = _userId, MinCashBufferPct = MinCash });
        _allocations.Setup(a => a.GetAllocationTargetsAsync(_userId, default)).ReturnsAsync([]);
    }

    private void GivenCashPct(decimal cashPct)
    {
        const decimal total = 10000m;
        _book.Setup(b => b.ReadAsync(_userId, default))
            .ReturnsAsync(new BookSnapshot(total, total * cashPct, [], false, [], total - (total * cashPct)));
    }

    [Fact]
    public async Task AckedPolicy_NotWorsened_IsSilenced()
    {
        GivenMinCashAck();
        GivenCashPct(CashPctAtAck - 0.01m);

        (await Reader().IsPolicySilencedAsync(_userId, RiskRuleKeys.MinCashBuffer)).Should().BeTrue();
    }

    [Fact]
    public async Task AckedPolicy_WorsenedPastStep_IsNoLongerSilenced()
    {
        GivenMinCashAck();
        GivenCashPct(CashPctAtAck - WorseningStep - 0.01m);

        (await Reader().IsPolicySilencedAsync(_userId, RiskRuleKeys.MinCashBuffer)).Should().BeFalse();
    }

    [Fact]
    public async Task AckedPolicy_NoCurrentViolation_IsSilenced()
    {
        GivenMinCashAck();
        GivenCashPct(MinCash + 0.05m);

        (await Reader().IsPolicySilencedAsync(_userId, RiskRuleKeys.MinCashBuffer)).Should().BeTrue();
    }

    [Fact]
    public async Task PolicyWithoutAck_IsNotSilenced()
    {
        GivenMinCashAck();
        GivenCashPct(CashPctAtAck);

        (await Reader().IsPolicySilencedAsync(_userId, RiskRuleKeys.MaxPositionWeight)).Should().BeFalse();
    }
}
