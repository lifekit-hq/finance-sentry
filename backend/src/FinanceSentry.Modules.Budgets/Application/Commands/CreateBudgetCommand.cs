namespace FinanceSentry.Modules.Budgets.Application.Commands;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Budgets.API.Responses;
using FinanceSentry.Modules.Budgets.Application.Estimates;
using FinanceSentry.Modules.Budgets.Application.Services;
using FinanceSentry.Modules.Budgets.Domain;
using FinanceSentry.Modules.Budgets.Domain.Exceptions;
using FinanceSentry.Modules.Budgets.Domain.Repositories;

public record CreateBudgetCommand(Guid UserId, string Category, decimal MonthlyLimit) : ICommand<BudgetDto>;

public class CreateBudgetCommandHandler(
    IBudgetRepository budgets,
    ICategoryNormalizationService normalization,
    IUserBaseCurrencyReader currencyReader)
    : ICommandHandler<CreateBudgetCommand, BudgetDto>
{
    private readonly IBudgetRepository _budgets = budgets;
    private readonly ICategoryNormalizationService _normalization = normalization;
    private readonly IUserBaseCurrencyReader _currencyReader = currencyReader;

    public async Task<BudgetDto> Handle(CreateBudgetCommand command, CancellationToken cancellationToken)
    {
        // An override sits over an estimate line, so only a line an estimate can exist for takes one.
        if (!EstimateLines.IsEstimable(command.Category))
            throw new BudgetInvalidCategoryException();

        if (command.MonthlyLimit <= 0)
            throw new BudgetInvalidLimitException();

        var existing = await _budgets.FindByUserAndCategoryAsync(
            command.UserId, command.Category, cancellationToken);
        if (existing is not null)
            throw new BudgetDuplicateCategoryException();

        var currency = await _currencyReader.GetAsync(command.UserId, cancellationToken);
        var budget = Budget.Create(command.UserId, command.Category, command.MonthlyLimit, currency);
        await _budgets.CreateAsync(budget, cancellationToken);

        return new BudgetDto(
            budget.Id,
            budget.Category,
            _normalization.GetLabel(budget.Category),
            budget.MonthlyLimit,
            budget.Currency,
            budget.CreatedAt);
    }
}
