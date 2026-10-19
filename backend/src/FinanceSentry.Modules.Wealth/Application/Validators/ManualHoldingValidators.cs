namespace FinanceSentry.Modules.Wealth.Application.Validators;

using FinanceSentry.Core.Utils;
using FinanceSentry.Modules.Wealth.Application.Commands;
using FinanceSentry.Modules.Wealth.Domain;
using FluentValidation;

/// <summary>
/// The bounds of a hand-entered value: a label, a positive amount in a currency the rate table knows (an unknown one
/// would convert 1:1), a date that is not in the future, and an explicit convertibility.
/// </summary>
public sealed class ManualHoldingInputValidator : AbstractValidator<ManualHoldingInput>
{
    public ManualHoldingInputValidator()
    {
        RuleFor(x => x.Label)
            .Must(l => !string.IsNullOrWhiteSpace(l))
            .WithMessage("Label is required.")
            .Must(l => l is null || l.Trim().Length <= ManualHoldingLimits.LabelMaxLength)
            .WithMessage($"Label must be at most {ManualHoldingLimits.LabelMaxLength} characters.");

        RuleFor(x => x.Amount)
            .NotNull()
            .WithMessage("Amount is required.")
            .Must(a => a is null || a.Value is > 0m and < ManualHoldingLimits.MaxAmount)
            .WithMessage("Amount must be greater than zero.");

        RuleFor(x => x.Currency)
            .Must(c => c is { Length: 3 } && CurrencyConverter.IsKnown(c))
            .WithMessage("Currency must be a known three-letter code.");

        // A day of slack: the person's today can be ahead of UTC's.
        RuleFor(x => x.AsOf)
            .NotNull()
            .WithMessage("As-of date is required.")
            .Must(d => d is null || d.Value <= DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1))
            .WithMessage("As-of date cannot be in the future.");

        RuleFor(x => x.Convertibility)
            .NotNull()
            .WithMessage("Convertibility is required.")
            .IsInEnum()
            .WithMessage("Convertibility must be Cash, InEconomy or Locked.");
    }
}

public sealed class CreateManualHoldingCommandValidator : AbstractValidator<CreateManualHoldingCommand>
{
    public CreateManualHoldingCommandValidator()
        => RuleFor(x => x.Input).NotNull().SetValidator(new ManualHoldingInputValidator());
}

public sealed class UpdateManualHoldingCommandValidator : AbstractValidator<UpdateManualHoldingCommand>
{
    public UpdateManualHoldingCommandValidator()
        => RuleFor(x => x.Input).NotNull().SetValidator(new ManualHoldingInputValidator());
}
