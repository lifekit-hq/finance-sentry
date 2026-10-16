namespace FinanceSentry.Modules.BankSync.Application.Services;

using FinanceSentry.Core.Connections;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.BankSync.Application.Connections;
using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Domain.Repositories;
using Microsoft.Extensions.Logging;

/// <summary>
/// The card and account lifecycle discovery drives, on top of one provider connection's successful account-list read:
/// an account the list stops naming is marked absent, leaves live totals after
/// <see cref="BankAccount.AbsenceExclusionThreshold"/> and is retired once its own health also says Dormant/ResourceGone
/// (two independent signals); one that comes back in the list is restored; a new account whose IBAN equals a failing
/// or absent account's is that account's reissue and retires it at once.
///
/// Every write goes through the unscoped lifecycle save (a targeted update of the lifecycle columns), so a stale tracked
/// row can never overwrite a concurrent sync, and discovery runs with no principal.
/// </summary>
public interface IAccountLifecycle
{
    /// <summary>
    /// Reconciles the connection's held accounts (closed ones included) with what its provider listed this run.
    /// Call only after a complete, successful read; a list that names none of the held accounts is ignored.
    /// </summary>
    Task ReconcileAsync(
        IReadOnlyCollection<BankAccount> connectionAccounts, IReadOnlySet<string> listedIds, CancellationToken ct = default);

    /// <summary>
    /// Stores a newly discovered account. When its IBAN exactly equals a failing or absent account of the same
    /// connection, the old one is retired on the spot and the new one records it as its predecessor.
    /// </summary>
    Task AddDiscoveredAsync(
        BankAccount created, IReadOnlyCollection<BankAccount> connectionAccounts, IReadOnlySet<string> listedIds,
        CancellationToken ct = default);

    /// <summary>Brings a retired account back: clears the closure and the absence mark, and its alerts follow the next sync.</summary>
    Task ReopenAsync(BankAccount account, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class AccountLifecycle(
    IBankAccountRepository accounts,
    IAlertGeneratorService alerts,
    TimeProvider time,
    ILogger<AccountLifecycle> logger) : IAccountLifecycle
{
    /// <inheritdoc />
    public async Task ReconcileAsync(
        IReadOnlyCollection<BankAccount> connectionAccounts, IReadOnlySet<string> listedIds, CancellationToken ct = default)
    {
        // One empty or truncated answer must not flag everything: the list has to name a held account.
        if (!connectionAccounts.Any(a => listedIds.Contains(a.ExternalAccountId)))
            return;

        var now = time.GetUtcNow();
        foreach (var account in connectionAccounts)
        {
            if (listedIds.Contains(account.ExternalAccountId))
                await RestoreAsync(account, ct);
            else
                await MarkAbsentAsync(account, now, ct);
        }
    }

    /// <inheritdoc />
    public async Task AddDiscoveredAsync(
        BankAccount created, IReadOnlyCollection<BankAccount> connectionAccounts, IReadOnlySet<string> listedIds,
        CancellationToken ct = default)
    {
        var predecessor = FindExactIbanPredecessor(created, connectionAccounts, listedIds);
        if (predecessor is not null)
            created.PredecessorAccountId = predecessor.Id;

        await accounts.AddAsync(created, ct);

        if (predecessor is not null)
        {
            logger.LogWarning(
                "Account {AccountId} ({Provider}) reissues {PredecessorId}: exact IBAN match, retiring the old one now.",
                created.Id, created.Provider, predecessor.Id);
            await RetireAsync(predecessor, AccountClosedReasons.Reissued, created, ct);
        }
        else
        {
            SuggestReissue(created, connectionAccounts, listedIds);
        }
    }

    /// <inheritdoc />
    public async Task ReopenAsync(BankAccount account, CancellationToken ct = default)
    {
        account.Reopen();
        await accounts.SaveLifecycleUnscopedAsync(account, ct);
        await accounts.SaveHealthUnscopedAsync(account.Id, account.Health, ct);
    }

    // Back in the list: the absence mark clears, and a system closure for absence undoes itself. A reissue's closure
    // stays: its successor carries the balance, and reopening both would count it twice.
    private async Task RestoreAsync(BankAccount account, CancellationToken ct)
    {
        if (account.IsClosed)
        {
            if (account.ClosedReason != AccountClosedReasons.AbsentFromList
                || account.ClosedBy != AccountClosedReasons.BySystem)
                return;

            logger.LogWarning(
                "Account {AccountId} ({Provider}) is listed again; reopening it (retired for absence).",
                account.Id, account.Provider);
            await ReopenAsync(account, ct);
            return;
        }

        if (account.AbsentSince is null)
            return;

        account.AbsentSince = null;
        await accounts.SaveLifecycleUnscopedAsync(account, ct);
    }

    private async Task MarkAbsentAsync(BankAccount account, DateTimeOffset now, CancellationToken ct)
    {
        if (account.IsClosed)
            return;

        if (account.AbsentSince is null)
        {
            account.AbsentSince = now;
            await accounts.SaveLifecycleUnscopedAsync(account, ct);
            logger.LogInformation(
                "Account {AccountId} ({Provider}) is absent from its connection's list; its balance leaves live totals in {Hours}h unless it returns.",
                account.Id, account.Provider, BankAccount.AbsenceExclusionThreshold.TotalHours);
            return;
        }

        // Retire needs both signals plus the absence window: health Dormant (failing for DormantAfter) with a
        // ResourceGone class, and the list that no longer names it.
        if (account.IsVanished(now)
            && account.Health.State == ConnectionHealthState.Dormant
            && account.Health.LastFailureClass == FailureClass.ResourceGone)
            await RetireAsync(account, AccountClosedReasons.AbsentFromList, replacedBy: null, ct);
    }

    private async Task RetireAsync(BankAccount account, string reason, BankAccount? replacedBy, CancellationToken ct)
    {
        account.Retire(time.GetUtcNow(), reason);
        await accounts.SaveLifecycleUnscopedAsync(account, ct);

        logger.LogWarning(
            "Retired account {AccountId} ({Provider} {ExternalAccountId} ···{Last4}): {Reason}.",
            account.Id, account.Provider, account.ExternalAccountId, account.AccountNumberLast4, reason);

        // The retirement is stored; the alerts are best-effort so a failing alert write never undoes it.
        try
        {
            await alerts.ResolveSyncFailureAlertAsync(account.UserId, account.Provider, account.Id, ct);
            await alerts.ResolveConnectionDormantAlertAsync(account.UserId, account.Provider, account.Id, ct);
            await alerts.ResolveCashShortfallAlertAsync(account.UserId, account.Id, ct);
            await alerts.ResolveLowBalanceAlertAsync(account.UserId, account.Id, ct);
            await alerts.GenerateAccountRetiredAlertAsync(
                account.UserId, account.Id, DisplayName(account), replacedBy is null ? null : DisplayName(replacedBy), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not update the alerts of retired account {AccountId}.", account.Id);
        }
    }

    private static BankAccount? FindExactIbanPredecessor(
        BankAccount created, IReadOnlyCollection<BankAccount> connectionAccounts, IReadOnlySet<string> listedIds)
    {
        var iban = NormalizeIban(created.Iban);
        if (iban is null)
            return null;

        // Only an account that is failing or absent hands over: two healthy listed accounts sharing an IBAN are not a reissue.
        return connectionAccounts.FirstOrDefault(old =>
            !old.IsClosed
            && old.Provider == created.Provider
            && NormalizeIban(old.Iban) == iban
            && IsFailingOrAbsent(old, listedIds));
    }

    // TrueLayer matches without an IBAN (a card's last four, type and currency) are only a hint: they are logged for
    // the person to confirm, never linked or retired.
    private void SuggestReissue(
        BankAccount created, IReadOnlyCollection<BankAccount> connectionAccounts, IReadOnlySet<string> listedIds)
    {
        if (created.Provider != TrueLayerFailureClassifier.ProviderName)
            return;

        foreach (var old in connectionAccounts.Where(old =>
            !old.IsClosed
            && old.Provider == created.Provider
            && old.AccountType == created.AccountType
            && old.Currency == created.Currency
            && old.AccountNumberLast4 == created.AccountNumberLast4
            && IsFailingOrAbsent(old, listedIds)))
        {
            logger.LogInformation(
                "Account {AccountId} may reissue {PredecessorId} ({Provider}, same type, currency and last four); not linked, no exact IBAN.",
                created.Id, old.Id, created.Provider);
        }
    }

    private static bool IsFailingOrAbsent(BankAccount account, IReadOnlySet<string> listedIds)
        => !listedIds.Contains(account.ExternalAccountId)
            || account.Health.State != ConnectionHealthState.Healthy
            || account.SyncStatus is "failed" or "reauth_required";

    private static string? NormalizeIban(string? iban)
    {
        var normalized = iban?.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }

    private static string DisplayName(BankAccount account) => $"{account.BankName} ···{account.AccountNumberLast4}";
}
