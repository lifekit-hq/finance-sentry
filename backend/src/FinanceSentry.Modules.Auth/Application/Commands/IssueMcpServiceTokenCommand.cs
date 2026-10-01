using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Auth.Application.Interfaces;
using FinanceSentry.Modules.Auth.Domain.Entities;
using FinanceSentry.Modules.Auth.Domain.Exceptions;
using Microsoft.AspNetCore.Identity;

namespace FinanceSentry.Modules.Auth.Application.Commands;

public sealed record IssueMcpServiceTokenCommand(string RawRefreshToken, string? Label, int? LifetimeDays)
    : ICommand<IssueMcpServiceTokenResult>;

public sealed record IssueMcpServiceTokenResult(string Token, string Jti, DateTime ExpiresAt, string Label);

public sealed class IssueMcpServiceTokenCommandHandler(
    IRefreshTokenService refreshTokenService,
    IMcpServiceTokenStore serviceTokenStore,
    ITokenService tokenService,
    UserManager<ApplicationUser> userManager)
    : ICommandHandler<IssueMcpServiceTokenCommand, IssueMcpServiceTokenResult>
{
    private const int DefaultLifetimeDays = 180;
    private const int MaxLifetimeDays = 365;

    public async Task<IssueMcpServiceTokenResult> Handle(IssueMcpServiceTokenCommand request, CancellationToken cancellationToken)
    {
        var existing = await refreshTokenService.ValidateUnscopedAsync(request.RawRefreshToken, cancellationToken)
            ?? throw new InvalidRefreshTokenException("No session found.");

        var user = await userManager.FindByIdAsync(existing.UserId)
            ?? throw new InvalidRefreshTokenException("No session found.");

        var label = string.IsNullOrWhiteSpace(request.Label) ? "mcp-service" : request.Label.Trim();
        var lifetimeDays = Math.Clamp(request.LifetimeDays ?? DefaultLifetimeDays, 1, MaxLifetimeDays);

        var (token, jti, expiresAt) = tokenService.GenerateMcpServiceToken(user, lifetimeDays);
        await serviceTokenStore.AddAsync(new McpServiceToken(jti, user.Id, label, expiresAt), cancellationToken);

        return new IssueMcpServiceTokenResult(token, jti.ToString(), expiresAt, label);
    }
}
