using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Auth.Application.Interfaces;
using FinanceSentry.Modules.Auth.Domain.Entities;
using FinanceSentry.Modules.Auth.Domain.Exceptions;
using FinanceSentry.Modules.Auth.Domain.People;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Identity;

namespace FinanceSentry.Modules.Auth.Application.Commands;

/// <summary>
/// Creates a Member account without a password. Inviting an email whose invite is still pending returns that
/// invite unchanged; an email that already has an active or revoked account is refused.
/// </summary>
public class CreateInviteCommandHandler(
    UserManager<ApplicationUser> userManager,
    IUserAccessService userAccess,
    IEventBus eventBus) : ICommandHandler<CreateInviteCommand, InviteDto>
{
    public async Task<InviteDto> Handle(CreateInviteCommand request, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim();
        var user = await userManager.FindByEmailAsync(email);

        if (user is null)
        {
            user = new ApplicationUser { UserName = email, Email = email };
            var created = await userManager.CreateAsync(user);
            if (!created.Succeeded)
                throw new ValidationException(
                    created.Errors.Select(e => new ValidationFailure(nameof(request.Email), e.Description)));

            await userAccess.GrantDefaultRoleAsync(user);
            await PublishUserRegisteredAsync(user.Id, cancellationToken);
        }
        else
        {
            var hasExternalLogin = (await userManager.GetLoginsAsync(user)).Count > 0;
            if (PersonStatus.Of(user, hasExternalLogin) != PersonStatus.Invited)
                throw new DuplicateEmailException();
        }

        return new InviteDto(user.Id, user.Email!);
    }

    /// <summary>
    /// Best-effort: a downstream module's per-user provisioning (e.g. Companion notification
    /// settings, issue #686) must not fail the invite.
    /// </summary>
    private async Task PublishUserRegisteredAsync(string userId, CancellationToken cancellationToken)
    {
        try
        {
            await eventBus.Publish(new UserRegisteredEvent(Guid.Parse(userId)), cancellationToken);
        }
        catch
        {
            // best-effort
        }
    }
}
