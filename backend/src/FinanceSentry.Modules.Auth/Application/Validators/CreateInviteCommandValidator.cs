using FinanceSentry.Modules.Auth.Application.Commands;
using FluentValidation;

namespace FinanceSentry.Modules.Auth.Application.Validators;

public sealed class CreateInviteCommandValidator : AbstractValidator<CreateInviteCommand>
{
    public CreateInviteCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("Email must be a valid email address.");
    }
}
