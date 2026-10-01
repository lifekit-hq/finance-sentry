namespace FinanceSentry.Modules.Auth.Application.Interfaces;

public interface IMcpAuthorizationCodeStore
{
    Task<string> IssueAsync(string userId, string email, string redirectUri, CancellationToken cancellationToken = default);

    /// <summary>
    /// Redeems a code once for the redirect URI it was issued to. Ignores the Owner query filter: the code is redeemed
    /// at the anonymous token endpoint, before any person is in scope.
    /// </summary>
    Task<McpAuthorizationCodePayload?> ConsumeUnscopedAsync(string code, string redirectUri, CancellationToken cancellationToken = default);
}
