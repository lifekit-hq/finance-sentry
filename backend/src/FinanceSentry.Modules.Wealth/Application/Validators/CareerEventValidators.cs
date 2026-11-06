namespace FinanceSentry.Modules.Wealth.Application.Validators;

using FinanceSentry.Core.Utils;
using FinanceSentry.Modules.Wealth.Application.Commands;
using FinanceSentry.Modules.Wealth.Domain;
using FluentValidation;

/// <summary>
/// The bounds of a career event: a kind, a date no more than <see cref="CareerEventLimits.MaxYearsAhead"/> years ahead, a
/// label, a currency the rate table knows (an unknown one would convert 1:1), an amount (signed and non-zero for an income
/// step, positive for the rest), and an end date for a leave - and only for a leave.
/// </summary>
public sealed class CareerEventInputValidator : AbstractValidator<CareerEventInput>
{
    public CareerEventInputValidator(TimeProvider? clock = null)
    {
        var time = clock ?? TimeProvider.System;

        RuleFor(x => x.Kind)
            .NotNull().WithMessage("Kind is required.")
            .IsInEnum().WithMessage("Kind must be IncomeStep, OneOffIn, OneOffOut or Leave.");

        RuleFor(x => x.Date)
            .NotNull().WithMessage("Date is required.")
            .Must(d => d is null || d.Value <= DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime).AddYears(CareerEventLimits.MaxYearsAhead))
            .WithMessage($"Date can be at most {CareerEventLimits.MaxYearsAhead} years ahead.");

        RuleFor(x => x.Label)
            .Must(l => !string.IsNullOrWhiteSpace(l))
            .WithMessage("Label is required.")
            .Must(l => l is null || l.Trim().Length <= CareerEventLimits.LabelMaxLength)
            .WithMessage($"Label must be at most {CareerEventLimits.LabelMaxLength} characters.");

        RuleFor(x => x.Note)
            .Must(n => n is null || n.Trim().Length <= CareerEventLimits.NoteMaxLength)
            .WithMessage($"Note must be at most {CareerEventLimits.NoteMaxLength} characters.");

        RuleFor(x => x.Currency)
            .Must(c => c is { Length: 3 } && CurrencyConverter.IsKnown(c))
            .WithMessage("Currency must be a known three-letter code.");

        RuleFor(x => x.Amount)
            .NotNull().WithMessage("Amount is required.")
            .Must(a => a is null || Math.Abs(a.Value) < CareerEventLimits.MaxAmount)
            .WithMessage("Amount is too large.")
            .Must((input, a) => a is null || input.Kind != CareerEventKind.IncomeStep || a.Value != 0m)
            .WithMessage("An income step must change income by something.")
            .Must((input, a) => a is null || input.Kind is null || input.Kind == CareerEventKind.IncomeStep || a.Value > 0m)
            .WithMessage("Amount must be greater than zero.");

        RuleFor(x => x.EndDate)
            .Must((input, end) => input.Kind != CareerEventKind.Leave || end is not null)
            .WithMessage("A leave needs an end date.")
            .Must((input, end) => input.Kind == CareerEventKind.Leave || end is null)
            .WithMessage("Only a leave has an end date.")
            .Must((input, end) => end is null || input.Date is null || end.Value >= input.Date.Value)
            .WithMessage("End date cannot be before the date.");
    }
}

public sealed class CreateCareerEventCommandValidator : AbstractValidator<CreateCareerEventCommand>
{
    public CreateCareerEventCommandValidator(TimeProvider clock)
        => RuleFor(x => x.Input).NotNull().SetValidator(new CareerEventInputValidator(clock));
}

public sealed class UpdateCareerEventCommandValidator : AbstractValidator<UpdateCareerEventCommand>
{
    public UpdateCareerEventCommandValidator(TimeProvider clock)
        => RuleFor(x => x.Input).NotNull().SetValidator(new CareerEventInputValidator(clock));
}
