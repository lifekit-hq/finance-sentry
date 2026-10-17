namespace FinanceSentry.Modules.Auth.Infrastructure.Authorization;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Auth.Application.Interfaces;
using FinanceSentry.Modules.Auth.Domain.Entities;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Identity;

public sealed class MemberAccountCreator(
    UserManager<ApplicationUser> users,
    IUserAccessService userAccess,
    IEventBus eventBus) : IMemberAccountCreator
{
    public async Task<ApplicationUser> CreateAsync(string email, CancellationToken cancellationToken)
    {
        var user = new ApplicationUser { UserName = email, Email = email };
        var created = await users.CreateAsync(user);
        if (!created.Succeeded)
            throw new ValidationException(
                created.Errors.Select(e => new ValidationFailure("Email", e.Description)));

        await userAccess.GrantDefaultRoleAsync(user);
        await PublishUserRegisteredAsync(user.Id, cancellationToken);
        return user;
    }

    /// <summary>
    /// Best-effort: a downstream module's per-user provisioning (e.g. Companion notification
    /// settings, issue #686) must not fail the account creation.
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
