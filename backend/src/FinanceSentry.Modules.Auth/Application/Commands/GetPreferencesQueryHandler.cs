using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Auth.Domain.Entities;
using FinanceSentry.Modules.Auth.Domain.Exceptions;
using Microsoft.AspNetCore.Identity;

namespace FinanceSentry.Modules.Auth.Application.Commands;

public class GetPreferencesQueryHandler(UserManager<ApplicationUser> userManager)
    : IQueryHandler<GetPreferencesQuery, PreferencesDto>
{
    public async Task<PreferencesDto> Handle(GetPreferencesQuery query, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(query.UserId.ToString())
            ?? throw new UserNotFoundException();

        return ToDto(user);
    }

    internal static PreferencesDto ToDto(ApplicationUser user) =>
        new(new AppearanceDto(
            ThemeModes.IsKnown(user.Theme) ? user.Theme : ThemeModes.System,
            user.ThemeSeed,
            user.ThemeIntensity));
}
