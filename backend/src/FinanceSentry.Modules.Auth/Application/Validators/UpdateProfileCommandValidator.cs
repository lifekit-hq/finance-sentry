using FinanceSentry.Modules.Auth.Application.Commands;
using FluentValidation;

namespace FinanceSentry.Modules.Auth.Application.Validators;

/// <summary>
/// The FIRE assumption bounds, as fractions, mirroring the Settings screen (withdrawal rate 0.5-10%, real
/// return 0-15%). A non-positive withdrawal rate would collapse the FIRE target to zero and report "already
/// reached", so the API refuses it rather than trusting the client.
/// </summary>
public sealed class UpdateProfileCommandValidator : AbstractValidator<UpdateProfileCommand>
{
    public const decimal SafeWithdrawalRateMin = 0.005m;
    public const decimal SafeWithdrawalRateMax = 0.10m;
    public const decimal RealAnnualReturnMin = 0m;
    public const decimal RealAnnualReturnMax = 0.15m;

    public UpdateProfileCommandValidator()
    {
        RuleFor(x => x.SafeWithdrawalRate)
            .InclusiveBetween(SafeWithdrawalRateMin, SafeWithdrawalRateMax)
            .WithMessage("Safe withdrawal rate must be between 0.5% and 10%.");

        RuleFor(x => x.RealAnnualReturn)
            .InclusiveBetween(RealAnnualReturnMin, RealAnnualReturnMax)
            .WithMessage("Real annual return must be between 0% and 15%.");
    }
}
