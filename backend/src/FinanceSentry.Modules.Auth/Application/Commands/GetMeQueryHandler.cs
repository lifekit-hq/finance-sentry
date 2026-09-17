using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Auth.Application.Interfaces;
using FinanceSentry.Modules.Auth.Domain.Entities;
using FinanceSentry.Modules.Auth.Domain.Exceptions;
using Microsoft.AspNetCore.Identity;

namespace FinanceSentry.Modules.Auth.Application.Commands;

public class GetMeQueryHandler(
    IRefreshTokenService refreshTokenService,
    ITokenService tokenService,
    UserManager<ApplicationUser> userManager) : IQueryHandler<GetMeQuery, GetMeResult>
{
    public async Task<GetMeResult> Handle(GetMeQuery request, CancellationToken cancellationToken)
    {
        var existing = await refreshTokenService.ValidateAsync(request.RawRefreshToken, cancellationToken)
            ?? throw new InvalidRefreshTokenException();

        var user = await userManager.FindByIdAsync(existing.UserId)
            ?? throw new InvalidRefreshTokenException();

        var (accessToken, expiresAt) = tokenService.GenerateToken(user);
        var profile = GetProfileQueryHandler.ToDto(user);

        return new GetMeResult(new MeResponse(new UserDto(user.Id, user.Email!), expiresAt, profile), accessToken);
    }
}
