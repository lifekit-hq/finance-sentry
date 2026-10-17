using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Auth.Application.Interfaces;
using FinanceSentry.Modules.Auth.Domain.Entities;
using FinanceSentry.Modules.Auth.Domain.Exceptions;
using FinanceSentry.Modules.Auth.Domain.People;
using Microsoft.AspNetCore.Identity;

namespace FinanceSentry.Modules.Auth.Application.Commands;

/// <summary>
/// Creates a Member account without a password. Inviting an email whose invite is still pending returns that
/// invite unchanged; an email that already has an active or revoked account is refused.
/// </summary>
public class CreateInviteCommandHandler(
    UserManager<ApplicationUser> userManager,
    IMemberAccountCreator memberAccounts) : ICommandHandler<CreateInviteCommand, InviteDto>
{
    public async Task<InviteDto> Handle(CreateInviteCommand request, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim();
        var user = await userManager.FindByEmailAsync(email);

        if (user is null)
        {
            user = await memberAccounts.CreateAsync(email, cancellationToken);
        }
        else
        {
            var hasExternalLogin = (await userManager.GetLoginsAsync(user)).Count > 0;
            if (PersonStatus.Of(user, hasExternalLogin) != PersonStatus.Invited)
                throw new DuplicateEmailException();
        }

        return new InviteDto(user.Id, user.Email!);
    }
}
