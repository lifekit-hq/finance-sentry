using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Auth.Domain.Entities;
using FinanceSentry.Modules.Auth.Domain.Exceptions;
using Microsoft.AspNetCore.Identity;

namespace FinanceSentry.Modules.Auth.Application.Commands;

public class ChangePasswordCommandHandler(UserManager<ApplicationUser> userManager)
    : ICommandHandler<ChangePasswordCommand, Unit>
{
    public async Task<Unit> Handle(ChangePasswordCommand command, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(command.UserId.ToString())
            ?? throw new UserNotFoundException();

        if (user.PasswordHash is null)
            throw new ExternalAccountOnlyException();

        var result = await userManager.ChangePasswordAsync(user, command.CurrentPassword, command.NewPassword);
        if (!result.Succeeded)
            throw new InvalidCurrentPasswordException();

        return Unit.Value;
    }
}
