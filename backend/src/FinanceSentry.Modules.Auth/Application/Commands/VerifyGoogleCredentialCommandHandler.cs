namespace FinanceSentry.Modules.Auth.Application.Commands;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Auth.Application.Interfaces;
using FinanceSentry.Modules.Auth.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

public class VerifyGoogleCredentialCommandHandler(
    UserManager<ApplicationUser> userManager,
    ITokenService tokenService,
    IRefreshTokenService refreshTokenService,
    IGoogleCredentialVerifier verifier) : ICommandHandler<VerifyGoogleCredentialCommand, AuthResult>
{
    public async Task<AuthResult> Handle(VerifyGoogleCredentialCommand request, CancellationToken cancellationToken)
    {
        var googleUser = await verifier.VerifyAsync(request.Credential);

        var user = await userManager.Users.FirstOrDefaultAsync(u => u.GoogleId == googleUser.GoogleId, cancellationToken);

        if (user is null)
        {
            user = await userManager.FindByEmailAsync(googleUser.Email);
            if (user is not null && string.IsNullOrEmpty(user.GoogleId))
            {
                user.GoogleId = googleUser.GoogleId;
                await userManager.UpdateAsync(user);
            }
        }

        if (user is null)
        {
            user = new ApplicationUser
            {
                UserName = googleUser.Email,
                Email = googleUser.Email,
                GoogleId = googleUser.GoogleId,
                EmailConfirmed = true
            };
            var result = await userManager.CreateAsync(user);

            if (!result.Succeeded)
                throw new InvalidOperationException("VALIDATION_ERROR:" + string.Join("|", result.Errors.Select(e => e.Description)));
        }

        var (accessToken, expiresAt) = tokenService.GenerateToken(user);

        var (rawRefreshToken, _) = await refreshTokenService.IssueAsync(user.Id, cancellationToken);

        return new AuthResult(new AuthResponse(new UserDto(user.Id, user.Email!), expiresAt), rawRefreshToken, accessToken);
    }
}
