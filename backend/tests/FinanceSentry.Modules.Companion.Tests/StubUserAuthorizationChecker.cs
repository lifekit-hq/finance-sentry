namespace FinanceSentry.Modules.Companion.Tests;

using FinanceSentry.Core.Interfaces;

/// <summary>Passes every policy for the listed users and fails it for everyone else.</summary>
internal sealed class StubUserAuthorizationChecker(params Guid[] authorizedUserIds) : IUserAuthorizationChecker
{
    public List<string> CheckedPolicies { get; } = [];

    public Task<bool> IsAuthorizedAsync(Guid userId, string policy, CancellationToken ct = default)
    {
        CheckedPolicies.Add(policy);
        return Task.FromResult(authorizedUserIds.Contains(userId));
    }
}
