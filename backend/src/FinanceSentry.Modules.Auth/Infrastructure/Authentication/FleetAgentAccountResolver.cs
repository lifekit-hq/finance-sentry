namespace FinanceSentry.Modules.Auth.Infrastructure.Authentication;

using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.Auth.API.Authentication;
using FinanceSentry.Modules.Auth.Application.Interfaces;
using FinanceSentry.Modules.Auth.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

/// <inheritdoc />
public sealed class FleetAgentAccountResolver(
    UserManager<ApplicationUser> users,
    IOptions<FleetAgentOptions> options) : IFleetAgentAccountResolver
{
    public async Task<Guid?> ResolveAsync(CancellationToken cancellationToken = default)
    {
        IList<ApplicationUser> owners = await users.GetUsersInRoleAsync(AuthRoles.Owner);
        if (options.Value.ActAsUserId is { } configured)
            owners = [.. owners.Where(o => o.Id == configured.ToString())];

        // Ambiguity is checked before lockout: a locked-out Owner never makes another one the fleet's identity.
        if (owners.Count != 1 || await users.IsLockedOutAsync(owners[0]))
            return null;

        return Guid.TryParse(owners[0].Id, out var id) ? id : null;
    }
}
