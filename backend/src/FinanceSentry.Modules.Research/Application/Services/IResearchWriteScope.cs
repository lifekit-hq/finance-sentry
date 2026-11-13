namespace FinanceSentry.Modules.Research.Application.Services;

using FinanceSentry.Modules.Research.Domain;

/// <summary>
/// Makes a write and its journal entry one unit. A Ledger write runs in a single transaction, so a journal append
/// that fails rolls the write back and a retry starts clean; the owner's own write runs as-is, with the journal
/// best-effort. Re-entrant: a scope opened inside another joins it.
/// </summary>
public interface IResearchWriteScope
{
    Task<T> RunAsync<T>(ThesisEventOrigin origin, Func<Task<T>> work, CancellationToken ct = default);
}
