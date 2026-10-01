using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Auth.Domain.Entities;
using FinanceSentry.Modules.Auth.Domain.People;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FinanceSentry.Modules.Auth.Application.Commands;

public class ListPeopleQueryHandler(UserManager<ApplicationUser> userManager)
    : IQueryHandler<ListPeopleQuery, IReadOnlyList<PersonDto>>
{
    public async Task<IReadOnlyList<PersonDto>> Handle(ListPeopleQuery request, CancellationToken cancellationToken)
    {
        var users = await userManager.Users.OrderBy(u => u.Email).ToListAsync(cancellationToken);
        var people = new List<PersonDto>(users.Count);

        foreach (var user in users)
        {
            var roles = (await userManager.GetRolesAsync(user)).Order(StringComparer.Ordinal).ToList();
            var hasExternalLogin = (await userManager.GetLoginsAsync(user)).Count > 0;
            people.Add(new PersonDto(user.Id, user.Email!, roles, PersonStatus.Of(user, hasExternalLogin)));
        }

        return people;
    }
}
