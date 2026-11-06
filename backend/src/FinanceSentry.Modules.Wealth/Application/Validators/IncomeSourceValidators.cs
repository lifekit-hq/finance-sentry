namespace FinanceSentry.Modules.Wealth.Application.Validators;

using FinanceSentry.Core.Utils;
using FinanceSentry.Modules.Wealth.Application.Commands;
using FinanceSentry.Modules.Wealth.Domain;
using FluentValidation;

/// <summary>
/// The bounds of a declared income: a label, a positive amount in a currency the rate table knows (an unknown one would
/// convert 1:1), explicit kind, cadence and basis, and a start date with an end that does not precede it.
/// </summary>
public sealed class IncomeSourceInputValidator : AbstractValidator<IncomeSourceInput>
{
    public IncomeSourceInputValidator()
    {
        RuleFor(x => x.Kind)
            .NotNull().WithMessage("Kind is required.")
            .IsInEnum().WithMessage("Kind must be Salary, Side or Other.");

        RuleFor(x => x.Label)
            .Must(l => !string.IsNullOrWhiteSpace(l))
            .WithMessage("Label is required.")
            .Must(l => l is null || l.Trim().Length <= IncomeSourceLimits.LabelMaxLength)
            .WithMessage($"Label must be at most {IncomeSourceLimits.LabelMaxLength} characters.");

        RuleFor(x => x.Amount)
            .NotNull().WithMessage("Amount is required.")
            .Must(a => a is null || a.Value is > 0m and < IncomeSourceLimits.MaxAmount)
            .WithMessage("Amount must be greater than zero.");

        RuleFor(x => x.Currency)
            .Must(c => c is { Length: 3 } && CurrencyConverter.IsKnown(c))
            .WithMessage("Currency must be a known three-letter code.");

        RuleFor(x => x.Cadence)
            .NotNull().WithMessage("Cadence is required.")
            .IsInEnum().WithMessage("Cadence must be Monthly, Weekly, Fortnightly, Annual or Irregular.");

        RuleFor(x => x.GrossOrNet)
            .NotNull().WithMessage("Gross or net is required.")
            .IsInEnum().WithMessage("Gross or net must be Gross or Net.");

        RuleFor(x => x.StartDate)
            .NotNull().WithMessage("Start date is required.");

        RuleFor(x => x.EndDate)
            .Must((input, end) => end is null || input.StartDate is null || end.Value >= input.StartDate.Value)
            .WithMessage("End date cannot be before the start date.");
    }
}

public sealed class CreateIncomeSourceCommandValidator : AbstractValidator<CreateIncomeSourceCommand>
{
    public CreateIncomeSourceCommandValidator()
        => RuleFor(x => x.Input).NotNull().SetValidator(new IncomeSourceInputValidator());
}

public sealed class UpdateIncomeSourceCommandValidator : AbstractValidator<UpdateIncomeSourceCommand>
{
    public UpdateIncomeSourceCommandValidator()
        => RuleFor(x => x.Input).NotNull().SetValidator(new IncomeSourceInputValidator());
}
