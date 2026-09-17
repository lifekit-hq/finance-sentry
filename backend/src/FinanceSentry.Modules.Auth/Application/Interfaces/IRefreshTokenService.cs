using FinanceSentry.Modules.Auth.Domain.Entities;

namespace FinanceSentry.Modules.Auth.Application.Interfaces;

public interface IRefreshTokenService
{
    /// <summary>Issues a new refresh token for the given user. Returns the raw (unhashed) token and the stored entity.</summary>
    Task<(string RawToken, RefreshToken Entity)> IssueAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>Looks up a refresh token by its raw value. Returns null if not found, expired, or revoked.</summary>
    Task<RefreshToken?> ValidateAsync(string rawToken, CancellationToken cancellationToken = default);

    /// <summary>Revokes the existing token and issues a new one. Returns the new raw token and entity.</summary>
    Task<(string RawToken, RefreshToken Entity)> RotateAsync(RefreshToken existing, CancellationToken cancellationToken = default);

    /// <summary>Revokes all refresh tokens for the given user (bulk revoke, e.g. "sign out everywhere").</summary>
    Task RevokeAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>Revokes a specific refresh token when present.</summary>
    Task RevokeTokenAsync(string rawToken, CancellationToken cancellationToken = default);
}
