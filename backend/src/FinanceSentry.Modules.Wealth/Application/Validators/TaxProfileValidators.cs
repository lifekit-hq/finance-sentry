namespace FinanceSentry.Modules.Wealth.Application.Validators;

using FinanceSentry.Modules.Wealth.Application.Commands;
using FinanceSentry.Modules.Wealth.Domain;
using FluentValidation;

/// <summary>
/// The bounds of a tax profile: a two-letter jurisdiction code when one is given, bounded free text, and at least one value
/// (an all-blank request would store a row that says nothing; DELETE removes the profile).
/// </summary>
public sealed class TaxProfileInputValidator : AbstractValidator<TaxProfileInput>
{
    public TaxProfileInputValidator()
    {
        RuleFor(x => x.Jurisdiction)
            .Must(j => string.IsNullOrWhiteSpace(j) || IsCountryCode(j.Trim()))
            .WithMessage("Jurisdiction must be a two-letter country code.");

        RuleFor(x => x.JurisdictionNotes)
            .Must(t => t is null || t.Trim().Length <= TaxProfileLimits.TextMaxLength)
            .WithMessage($"Jurisdiction notes must be at most {TaxProfileLimits.TextMaxLength} characters.");

        RuleFor(x => x.Caveat)
            .Must(t => t is null || t.Trim().Length <= TaxProfileLimits.TextMaxLength)
            .WithMessage($"Caveat must be at most {TaxProfileLimits.TextMaxLength} characters.");

        RuleFor(x => x)
            .Must(x => !string.IsNullOrWhiteSpace(x.Jurisdiction) || !string.IsNullOrWhiteSpace(x.JurisdictionNotes)
                || !string.IsNullOrWhiteSpace(x.Caveat))
            .WithMessage("Give at least one of jurisdiction, jurisdiction notes or caveat.");
    }

    private static bool IsCountryCode(string value)
        => value.Length == TaxProfileLimits.JurisdictionLength && value.All(char.IsAsciiLetter);
}

public sealed class UpsertTaxProfileCommandValidator : AbstractValidator<UpsertTaxProfileCommand>
{
    public UpsertTaxProfileCommandValidator()
        => RuleFor(x => x.Input).NotNull().SetValidator(new TaxProfileInputValidator());
}
