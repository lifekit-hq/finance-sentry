namespace FinanceSentry.Modules.BankSync.Application.Services;

/// <summary>Outcome of one repair run; every count is rows, not amounts.</summary>
/// <param name="EligibleBefore">Retired pending rows the repair would restore, before the run.</param>
/// <param name="Reactivated">Rows made active again.</param>
/// <param name="Settled">Reactivated rows the provider reports as booked, marked settled.</param>
/// <param name="EligibleAfter">Eligible rows left after the run; 0 unless it was a dry run.</param>
/// <param name="AccountsUnread">Accounts whose provider could not be read: their rows come back pending and settle on a later sync.</param>
public sealed record RetiredPendingRepairResult(
    bool DryRun, int EligibleBefore, int Reactivated, int Settled, int EligibleAfter, int AccountsUnread);

/// <summary>
/// One-off repair for #1229: the pending reconciler used to retire a pending row whenever <i>any</i> posted row on the
/// account shared its amount and text, and the row's own settled copy then hashed like the retired one and was
/// dropped as a duplicate, so the payment vanished. This restores those rows.
///
/// <para>
/// Eligible: a pending row soft-deleted by the reconciler (no archive reason — an account removal or retention purge
/// stamps one) on or after 2026-07-23 11:20 UTC, the first run of that reconciler, on an active account, with
/// no active same-day twin (same account, type, amount and day) and no active booking the reconciler would still pair it
/// with (same account, amount and text, posted within <see cref="PendingReconciler.SettlementWindowDays"/> of it, one
/// booking per row). Each is reactivated; where the provider now reports
/// it as booked (a settled candidate with the row's hash) it is also marked settled.
/// </para>
///
/// <para>
/// Idempotent: a restored row is active, so a second run finds nothing eligible and changes nothing. It runs after
/// the date-aware reconciler is deployed, otherwise the next sync would retire the restored rows again.
/// </para>
/// </summary>
public interface IRetiredPendingRepairService
{
    /// <summary>Runs the repair, or only counts what it would restore when <paramref name="dryRun"/> is set.</summary>
    Task<RetiredPendingRepairResult> RepairAsync(bool dryRun, CancellationToken ct = default);
}
