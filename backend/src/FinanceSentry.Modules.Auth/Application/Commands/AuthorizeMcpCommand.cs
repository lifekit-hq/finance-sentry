using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Auth.Application.Interfaces;
using FinanceSentry.Modules.Auth.Domain.Entities;
using FinanceSentry.Modules.Auth.Domain.Exceptions;
using Microsoft.AspNetCore.Identity;

namespace FinanceSentry.Modules.Auth.Application.Commands;

public sealed record AuthorizeMcpCommand(string RawRefreshToken, string RedirectUri, string? State) : ICommand<AuthorizeMcpResult>;

public sealed record AuthorizeMcpResult(string RedirectUrl);

public sealed class AuthorizeMcpCommandHandler(
    IRefreshTokenService refreshTokenService,
    IMcpAuthorizationCodeStore mcpAuthorizationCodeStore,
    UserManager<ApplicationUser> userManager) : ICommandHandler<AuthorizeMcpCommand, AuthorizeMcpResult>
{
    public async Task<AuthorizeMcpResult> Handle(AuthorizeMcpCommand request, CancellationToken cancellationToken)
    {
        var redirectUri = McpLoopbackRedirectUri.Validate(request.RedirectUri);
        var existing = await refreshTokenService.ValidateUnscopedAsync(request.RawRefreshToken, cancellationToken)
            ?? throw new InvalidRefreshTokenException("No session found.");

        var user = await userManager.FindByIdAsync(existing.UserId)
            ?? throw new InvalidRefreshTokenException("No session found.");

        var code = await mcpAuthorizationCodeStore.IssueAsync(user.Id, user.Email!, redirectUri, cancellationToken);
        var separator = redirectUri.Contains('?') ? "&" : "?";
        var redirectUrl = $"{redirectUri}{separator}code={Uri.EscapeDataString(code)}";
        if (!string.IsNullOrWhiteSpace(request.State))
            redirectUrl += $"&state={Uri.EscapeDataString(request.State)}";

        return new AuthorizeMcpResult(redirectUrl);
    }
}
