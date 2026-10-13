using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Auth.Domain.Entities;
using FinanceSentry.Modules.Auth.Domain.Exceptions;
using Microsoft.AspNetCore.Identity;

namespace FinanceSentry.Modules.Auth.Application.Commands;

public class UpdatePreferencesCommandHandler(UserManager<ApplicationUser> userManager)
    : ICommandHandler<UpdatePreferencesCommand, PreferencesDto>
{
    public async Task<PreferencesDto> Handle(UpdatePreferencesCommand command, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(command.UserId.ToString())
            ?? throw new UserNotFoundException();

        if (command.Appearance is { } appearance)
        {
            // Written as a whole: an omitted seed or intensity clears the stored one.
            user.Theme = appearance.Theme;
            user.ThemeSeed = appearance.Seed?.ToLowerInvariant();
            user.ThemeIntensity = appearance.Intensity;
            await userManager.UpdateAsync(user);
        }

        return GetPreferencesQueryHandler.ToDto(user);
    }
}
