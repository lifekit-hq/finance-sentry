using FinanceSentry.Modules.Auth.Domain.Entities;

namespace FinanceSentry.Modules.Auth.Application.Interfaces;

public interface IMcpServiceTokenStore
{
    Task AddAsync(McpServiceToken token, CancellationToken cancellationToken = default);

    /// <summary>True when a service token with this jti exists, is not revoked, and has not expired.</summary>
    Task<bool> IsActiveAsync(Guid jti, CancellationToken cancellationToken = default);

    /// <summary>Marks the token revoked. Returns false when unknown or already revoked.</summary>
    Task<bool> RevokeAsync(Guid jti, CancellationToken cancellationToken = default);

    /// <summary>Marks every unrevoked token of the user revoked. Returns how many were revoked.</summary>
    Task<int> RevokeAllAsync(string userId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<McpServiceToken>> ListAsync(string userId, CancellationToken cancellationToken = default);
}
