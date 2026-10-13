using System.Text.RegularExpressions;
using FinanceSentry.Modules.Auth.Application.Commands;
using FluentValidation;

namespace FinanceSentry.Modules.Auth.Application.Validators;

/// <summary>
/// The appearance bounds of the app-shell preferences resource: a known theme mode, a <c>#rrggbb</c> seed and an
/// intensity in 0-1. Only the choices are stored, so anything else is refused rather than clamped.
/// </summary>
public sealed partial class UpdatePreferencesCommandValidator : AbstractValidator<UpdatePreferencesCommand>
{
    public UpdatePreferencesCommandValidator()
    {
        When(x => x.Appearance is not null, () =>
        {
            RuleFor(x => x.Appearance!.Theme)
                .Must(ThemeModes.IsKnown)
                .WithMessage("Theme must be system, light or dark.");

            RuleFor(x => x.Appearance!.Seed)
                .Must(seed => seed is null || HexColour().IsMatch(seed))
                .WithMessage("Seed must be a #rrggbb colour.");

            RuleFor(x => x.Appearance!.Intensity)
                .Must(intensity => intensity is null || intensity is >= 0d and <= 1d)
                .WithMessage("Intensity must be between 0 and 1.");
        });
    }

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex HexColour();
}
