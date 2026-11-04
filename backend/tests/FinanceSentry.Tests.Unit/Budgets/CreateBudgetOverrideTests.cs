namespace FinanceSentry.Tests.Unit.Budgets;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Budgets.Application.Commands;
using FinanceSentry.Modules.Budgets.Application.Services;
using FinanceSentry.Modules.Budgets.Domain;
using FinanceSentry.Modules.Budgets.Domain.Exceptions;
using FinanceSentry.Modules.Budgets.Domain.Repositories;
using Moq;
using Xunit;

/// <summary>
/// A budget row is a hand-set override over an estimate line, so only a line an estimate can exist for takes one:
/// never income or a transfer, which are not spending.
/// </summary>
public sealed class CreateBudgetOverrideTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly Mock<IBudgetRepository> _budgets = new();
    private readonly Mock<IUserBaseCurrencyReader> _currency = new();

    private CreateBudgetCommandHandler Handler()
    {
        _currency.Setup(c => c.GetAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync("USD");
        _budgets.Setup(b => b.CreateAsync(It.IsAny<Budget>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Budget b, CancellationToken _) => b);
        return new CreateBudgetCommandHandler(_budgets.Object, new CategoryNormalizationService(), _currency.Object);
    }

    [Theory]
    [InlineData("FOOD_AND_DRINK")]
    [InlineData("FAMILY_SUPPORT")]
    [InlineData("HOUSEHOLD")]
    [InlineData("LOAN_PAYMENTS")]
    [InlineData("UNCATEGORIZED")]
    public async Task ASpendingLine_TakesAnOverride(string category)
    {
        var dto = await Handler().Handle(new CreateBudgetCommand(UserId, category, 120m), CancellationToken.None);

        Assert.Equal(category, dto.Category);
        Assert.Equal(120m, dto.MonthlyLimit);
    }

    [Theory]
    [InlineData("INCOME")]
    [InlineData("TRANSFER_IN")]
    [InlineData("TRANSFER_OUT")]
    [InlineData("NOT_A_CATEGORY")]
    [InlineData("")]
    public async Task ALineThatIsNotSpending_IsRejected(string category)
    {
        await Assert.ThrowsAsync<BudgetInvalidCategoryException>(
            () => Handler().Handle(new CreateBudgetCommand(UserId, category, 120m), CancellationToken.None));

        _budgets.Verify(b => b.CreateAsync(It.IsAny<Budget>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ASecondOverrideOnTheSameLine_IsRejected()
    {
        var handler = Handler();
        _budgets.Setup(b => b.FindByUserAndCategoryAsync(UserId, "FOOD_AND_DRINK", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Budget.Create(UserId, "FOOD_AND_DRINK", 100m, "USD"));

        await Assert.ThrowsAsync<BudgetDuplicateCategoryException>(
            () => handler.Handle(new CreateBudgetCommand(UserId, "FOOD_AND_DRINK", 120m), CancellationToken.None));
    }
}
