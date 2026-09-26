using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Auth.Application.Interfaces;
using FinanceSentry.Modules.Auth.Domain.Entities;
using FinanceSentry.Modules.Auth.Domain.Exceptions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Identity;

namespace FinanceSentry.Modules.Auth.Application.Commands;

public class RegisterCommandHandler(
    UserManager<ApplicationUser> userManager,
    ITokenService tokenService,
    IRefreshTokenService refreshTokenService,
    IEventBus eventBus) : ICommandHandler<RegisterCommand, AuthResult>
{
    public async Task<AuthResult> Handle(RegisterCommand request, CancellationToken cancellationToken)
    {
        if (await userManager.FindByEmailAsync(request.Email) is not null)
            throw new DuplicateEmailException();

        var user = new ApplicationUser { UserName = request.Email, Email = request.Email };
        var result = await userManager.CreateAsync(user, request.Password);

        if (!result.Succeeded)
            throw new ValidationException(
                result.Errors.Select(e => new ValidationFailure(nameof(request.Password), e.Description)));

        await PublishUserRegisteredAsync(user.Id, cancellationToken);

        var (accessToken, expiresAt) = tokenService.GenerateToken(user);

        var (rawRefreshToken, _) = await refreshTokenService.IssueAsync(user.Id, cancellationToken);

        return new AuthResult(new AuthResponse(new UserDto(user.Id, user.Email!), expiresAt), rawRefreshToken, accessToken);
    }

    /// <summary>
    /// Best-effort: a downstream module's per-user provisioning (e.g. Companion notification
    /// settings, issue #686) must not fail registration.
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
