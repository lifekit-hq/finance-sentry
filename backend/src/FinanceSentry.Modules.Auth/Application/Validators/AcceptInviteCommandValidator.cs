using FinanceSentry.Modules.Auth.Application.Commands;
using FluentValidation;

namespace FinanceSentry.Modules.Auth.Application.Validators;

public sealed class AcceptInviteCommandValidator : AbstractValidator<AcceptInviteCommand>
{
    public AcceptInviteCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty().WithMessage("Invite link is incomplete.");
        RuleFor(x => x.Token).NotEmpty().WithMessage("Invite link is incomplete.");
        RuleFor(x => x.Password).NotEmpty().WithMessage("Password is required.");
    }
}
