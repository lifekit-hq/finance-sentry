namespace FinanceSentry.Core.Connections;

/// <summary>
/// One sync module's read of the health it stores on its own rows (slice S5): the read surfaces - the wealth summary
/// behind the Accounts page, the MCP health read - ask every module through this port instead of reading another
/// module's tables, and decide what to show with <see cref="ConnectionHealthSurface"/>. Reads only; there is no shared
/// table, each module answers from its own schema.
/// </summary>
public interface IConnectionHealthReader
{
    /// <summary>
    /// The stored health of <paramref name="userId"/>'s connections and, where a module keeps a sub-state, their
    /// accounts. Scoped by <paramref name="userId"/> itself, past the owner filter, so a caller with no principal reads
    /// the same rows as a request does.
    /// </summary>
    Task<IReadOnlyList<ConnectionHealthEntry>> GetForUserAsync(Guid userId, CancellationToken ct = default);
}

/// <summary>One row's stored health.</summary>
/// <param name="Provider">The provider, as the health log names it (<c>monobank</c>, <c>ibkr</c>, …).</param>
/// <param name="Kind">The row that carries the health (<c>BankAccount</c>, <c>IBKRCredential</c>, …).</param>
/// <param name="Id">That row's id.</param>
/// <param name="Health">Its stored health.</param>
/// <param name="IsAccount">An account's own sub-state rather than a connection's health.</param>
/// <param name="ConnectionId">For an account, the connection row it belongs to, when it has one.</param>
public sealed record ConnectionHealthEntry(
    string Provider, string Kind, Guid Id, ConnectionHealth Health, bool IsAccount = false, Guid? ConnectionId = null);
