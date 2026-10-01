using FinanceSentry.Modules.Risk.Application.Commands;
using FinanceSentry.Modules.Risk.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Xunit;

namespace FinanceSentry.Modules.Risk.Tests;

public sealed class SaveRiskRuleSetCommandTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    [Fact]
    public async Task WeightOutOfRange_IsRejected()
    {
        await using var db = TestSupport.NewContext(UserId);
        var handler = new SaveRiskRuleSetCommandHandler(new RiskRuleSetRepository(db));

        var command = new SaveRiskRuleSetCommand(UserId, 1.5m, null, null, null, null, null);

        var act = () => handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<RiskRuleSetValidationException>();
    }

    [Fact]
    public async Task NegativeTurnoverBudget_IsRejected()
    {
        await using var db = TestSupport.NewContext(UserId);
        var handler = new SaveRiskRuleSetCommandHandler(new RiskRuleSetRepository(db));

        var command = new SaveRiskRuleSetCommand(UserId, null, null, null, null, null, -1);

        var act = () => handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<RiskRuleSetValidationException>();
    }

    [Fact]
    public async Task ValidSave_AppendsNewVersion_AndFlipsIsCurrent()
    {
        await using var db = TestSupport.NewContext(UserId);
        var repo = new RiskRuleSetRepository(db);
        var handler = new SaveRiskRuleSetCommandHandler(repo);

        var first = await handler.Handle(new SaveRiskRuleSetCommand(UserId, 0.25m, null, null, null, null, null), CancellationToken.None);
        first.Version.Should().Be(1);

        var second = await handler.Handle(new SaveRiskRuleSetCommand(UserId, 0.3m, null, null, null, null, null), CancellationToken.None);
        second.Version.Should().Be(2);

        var current = await repo.GetCurrentAsync(UserId);
        current!.Version.Should().Be(2);
        current.MaxPositionWeightPct.Should().Be(0.3m);
    }

    [Fact]
    public async Task AllFieldsOptional_SaveIsAccepted()
    {
        await using var db = TestSupport.NewContext(UserId);
        var handler = new SaveRiskRuleSetCommandHandler(new RiskRuleSetRepository(db));

        var command = new SaveRiskRuleSetCommand(UserId, null, null, null, null, null, null);

        var saved = await handler.Handle(command, CancellationToken.None);

        saved.Version.Should().Be(1);
        saved.MaxPositionWeightPct.Should().BeNull();
    }

}
