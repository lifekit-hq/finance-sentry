namespace FinanceSentry.Modules.BankSync.Application.Connections;

using FinanceSentry.Core.Connections;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Infrastructure.Connections;
using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Domain.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// The connection-health policy for Monobank and TrueLayer (Option B, slice S2). Every attempt on a bank connection -
/// an account's sync, or the discovery pass's listing - is classified by the provider's
/// <see cref="IProviderFailureClassifier"/>, corrected by what the connection's other accounts and the provider's other
/// connections show, and run through <see cref="ConnectionHealthPolicy"/>.
/// <para>
/// Shadow mode is the default: the verdict is recorded and logged and nothing else, so the sync keeps its own alert and
/// account status. A provider listed in <see cref="ConnectionHealthOptions.ActiveProviders"/> is policy-driven: the
/// verdict decides its <c>SyncFailure</c> alert, replacing the per-failure alert the sync raised before, and the sync
/// parks an account (<c>reauth_required</c>) only on a confirmed credential loss.
/// </para>
/// <list type="bullet">
/// <item>The connection row (<see cref="MonobankCredential"/>: one token, many cards; <see cref="TrueLayerConnection"/>:
/// one consent, many accounts) carries the health that alerts, and its alert is keyed on the connection id, so a
/// token that dies raises one alert, not one per card.</item>
/// <item>Each <see cref="BankAccount"/> keeps its own streak of every attempt. It alerts, on the account id, only for a
/// failure the connection is not charged with: one account failing while the connection works (a sibling account's sync
/// or the discovery pass's listing succeeded during its streak).</item>
/// <item>Recording goes through the S1 recorder (<see cref="IConnectionHealthShadow"/>), which logs each evaluation and
/// stores the health; this class acts on the verdict it returns. Best-effort like the recorder: it never throws.</item>
/// </list>
/// </summary>
public interface IBankConnectionHealth
{
    /// <summary>
    /// Whether the policy drives <paramref name="provider"/>'s alerts and account status
    /// (<see cref="ConnectionHealthOptions.ActiveProviders"/>). A provider in shadow mode is still recorded here, but
    /// raises nothing through this class and resolves only the account's own alert on a success, as the sync always did:
    /// the sync keeps its own alerting.
    /// </summary>
    bool IsActive(string provider);

    /// <summary>Records a successful sync of <paramref name="account"/> on the account and its connection, and resolves their alerts.</summary>
    Task RecordSyncSuccessAsync(BankAccount account, CancellationToken ct = default);

    /// <summary>
    /// When a scheduled sync of <paramref name="account"/> may next call the provider, or null when it may go ahead: the
    /// provider asked for a wait (a rate limit's <c>Retry-After</c>, <see cref="ConnectionHealth.NotBefore"/>) that has
    /// not passed, on the account or on its connection, whose token the limit is charged to. A sync the person
    /// triggers never asks. Reads only; a connection that cannot be read pauses nothing.
    /// </summary>
    Task<DateTimeOffset?> ScheduledSyncPausedUntilAsync(BankAccount account, CancellationToken ct = default);

    /// <summary>
    /// The slowed cadence of a Dormant account or connection (<see cref="ConnectionHealthOptions.DormantSyncInterval"/>):
    /// when a scheduled sync of <paramref name="account"/> may next try it, or null when it may go ahead. Only for a
    /// provider in <see cref="ConnectionHealthOptions.ActiveProviders"/>; a shadow provider syncs as before and the
    /// would-be pause is logged. A sync the person triggers never asks.
    /// </summary>
    Task<DateTimeOffset?> DormantSyncPausedUntilAsync(BankAccount account, CancellationToken ct = default);

    /// <summary>The same slowed cadence for a scheduled connection-level call (the discovery pass's listing).</summary>
    DateTimeOffset? DormantPausedUntil(BankConnectionRef connection, ConnectionHealth health);

    /// <summary>Records a failed sync of <paramref name="account"/>, raises the alert the policy decides, and returns the verdict.</summary>
    /// <param name="cancelledByCaller">The sync was cancelled from our side (shutdown), which says nothing about the provider.</param>
    /// <param name="ct">Bookkeeping token; a failure path passes <see cref="CancellationToken.None"/> so a cancelled sync is still recorded.</param>
    Task<BankHealthVerdict> RecordSyncFailureAsync(
        BankAccount account, Exception exception, bool cancelledByCaller, CancellationToken ct = default);

    /// <summary>Records a successful connection-level call (the discovery pass's account listing) and resolves the connection's alert.</summary>
    Task RecordConnectionSuccessAsync(BankConnectionRef connection, CancellationToken ct = default);

    /// <summary>Records a failed connection-level call, raises the alert the policy decides, and returns the verdict.</summary>
    Task<BankHealthVerdict> RecordConnectionFailureAsync(
        BankConnectionRef connection, Exception exception, CancellationToken ct = default);

    /// <summary>
    /// Records a failed probe of the connection (<see cref="BankConnectionHealthProbe"/>): as a failed connection-level
    /// call, except that a credential failure on a connection already suspect confirms it
    /// (<see cref="ConnectionHealthPolicy.RecordProbeFailure"/>), so the reconnect ask goes out at once.
    /// </summary>
    Task<BankHealthVerdict> RecordConnectionProbeFailureAsync(
        BankConnectionRef connection, Exception exception, CancellationToken ct = default);

    /// <summary>
    /// Resolves the sync-failure alerts, and any Dormant notice, keyed on connections a successful reconnect has replaced:
    /// their accounts now belong to the new connection, so nothing would ever resolve the old connection's alerts. A provider in
    /// shadow mode raised no connection-keyed alert, so there is nothing to resolve and this does nothing.
    /// </summary>
    Task ResolveReplacedConnectionAlertsAsync(
        Guid userId, string provider, IReadOnlyCollection<Guid> replacedConnectionIds, CancellationToken ct = default);
}

/// <summary>A bank connection row, for the connection-level calls.</summary>
/// <param name="Provider"><c>monobank</c> or <c>truelayer</c>.</param>
/// <param name="Id">The <see cref="MonobankCredential"/> or <see cref="TrueLayerConnection"/> id; the alert's reference id.</param>
/// <param name="UserId">Its owner.</param>
/// <param name="Label">What the alert calls it.</param>
public sealed record BankConnectionRef(string Provider, Guid Id, Guid UserId, string Label);

/// <summary>What the policy made of a failed attempt.</summary>
/// <param name="Failure">The failure as classified and corrected; its code is the attempt's error code.</param>
/// <param name="Connection">The connection's health after the attempt; null when the connection was not charged or could not be read.</param>
public sealed record BankHealthVerdict(ProviderFailure Failure, ConnectionHealth? Connection)
{
    /// <summary>The connection's credential is confirmed lost: only the user can restore it by reconnecting.</summary>
    public bool CredentialLost => Connection?.State == ConnectionHealthState.ActionRequired;
}

/// <inheritdoc />
public sealed class BankConnectionHealth(
    IConnectionHealthShadow recorder,
    MonobankFailureClassifier monobankClassifier,
    TrueLayerFailureClassifier trueLayerClassifier,
    IMonobankCredentialRepository monobankCredentials,
    ITrueLayerConnectionRepository trueLayerConnections,
    IBankAccountRepository accounts,
    IAlertGeneratorService alerts,
    IUserAlertPreferencesReader userPreferences,
    IOptionsMonitor<ConnectionHealthOptions> options,
    TimeProvider time,
    ILogger<BankConnectionHealth> logger) : IBankConnectionHealth
{
    /// <summary>
    /// Other connections of the provider that must be failing at once before a suspect credential failure is read as
    /// the provider's outage rather than this consent's loss.
    /// </summary>
    public const int ProviderWideMinOtherFailingConnections = 2;

    /// <summary>The error code an attempt we cancelled ourselves is recorded with.</summary>
    public const string CancelledCode = "SYNC_CANCELLED";

    public bool IsActive(string provider) =>
        options.CurrentValue.ActiveProviders.Contains(provider, StringComparer.OrdinalIgnoreCase);

    public async Task RecordSyncSuccessAsync(BankAccount account, CancellationToken ct = default)
    {
        var evaluation = await recorder.RecordSuccessAsync(
            AccountSubject(account), account.Health, (health, c) => SaveAccountHealthAsync(account, health, c), ct);
        if (Healed(evaluation))
            await ResolveDormantAsync(account.UserId, account.Provider, account.Id, ct);

        // Resolve unconditionally, not only on the policy's Resolve: it is one lookup, and it also clears an alert raised
        // before the policy drove them (keyed on the account) or one whose raise raced this success. In shadow mode this is
        // the sync's own resolve, exactly as before the policy existed.
        await ResolveAsync(account.UserId, account.Provider, account.Id, ct);

        if (ConnectionOf(account) is not { } connection)
            return;

        await RecordConnectionSuccessAsync(connection, ct);
    }

    public async Task<DateTimeOffset?> ScheduledSyncPausedUntilAsync(BankAccount account, CancellationToken ct = default)
    {
        var now = time.GetUtcNow();
        var accountPause = ConnectionHealthPolicy.NotBeforePausedUntil(account.Health, now);
        if (ConnectionOf(account) is not { } connection)
            return accountPause;

        var stored = await ReadAsync(connection, ct);
        var connectionPause = stored is null ? null : ConnectionHealthPolicy.NotBeforePausedUntil(stored.Health, now);
        return (accountPause, connectionPause) switch
        {
            (null, var other) => other,
            (var own, null) => own,
            var (own, other) => own >= other ? own : other,
        };
    }

    public async Task<DateTimeOffset?> DormantSyncPausedUntilAsync(BankAccount account, CancellationToken ct = default)
    {
        var now = time.GetUtcNow();
        var accountPause = ConnectionHealthScheduledRunGate.DormantPausedUntil(
            logger, AccountSubject(account), account.Health, now, options.CurrentValue);
        if (ConnectionOf(account) is not { } connection || await ReadAsync(connection, ct) is not { } stored)
            return accountPause;

        var connectionPause = DormantPausedUntil(connection, stored.Health);
        return accountPause is null || connectionPause > accountPause ? connectionPause : accountPause;
    }

    public DateTimeOffset? DormantPausedUntil(BankConnectionRef connection, ConnectionHealth health) =>
        ConnectionHealthScheduledRunGate.DormantPausedUntil(
            logger, ConnectionSubject(connection), health, time.GetUtcNow(), options.CurrentValue);

    public async Task<BankHealthVerdict> RecordSyncFailureAsync(
        BankAccount account, Exception exception, bool cancelledByCaller, CancellationToken ct = default)
    {
        try
        {
            return await RecordSyncFailureCoreAsync(account, exception, cancelledByCaller, ct);
        }
        catch (Exception ex)
        {
            // The sync's own bookkeeping (freeing its claim) runs after this, so nothing may escape.
            logger.LogWarning(ex, "Connection health: could not record the failed sync of account {AccountId}", account.Id);
            return new BankHealthVerdict(ProviderFailure.Internal(null), null);
        }
    }

    private async Task<BankHealthVerdict> RecordSyncFailureCoreAsync(
        BankAccount account, Exception exception, bool cancelledByCaller, CancellationToken ct)
    {
        var at = time.GetUtcNow();
        var connection = ConnectionOf(account);
        var stored = connection is null ? null : await ReadAsync(connection, ct);
        var failure = cancelledByCaller
            ? ProviderFailure.Internal(CancelledCode)
            : Classify(account.Provider, exception, at, stored);

        if (connection is null || stored is null)
        {
            await RecordOnAccountAsync(account, failure, ct);
            return new BankHealthVerdict(failure, null);
        }

        if (SiblingProvedConnectionWorks(failure, stored.Health, account.Health, at))
        {
            // The connection works, so this account alone is failing: it alerts on its own id. A credential or
            // unexplained failure is read as the account's resource being gone; a transient one stays transient.
            if (failure.Class is FailureClass.Credential or FailureClass.Unknown)
                failure = ProviderFailure.ResourceGone(failure.Code);

            var evaluation = await RecordOnAccountAsync(account, failure, ct);
            switch (evaluation?.Outcome)
            {
                case ConnectionHealthOutcome.NotifyFailing:
                    await RaiseAsync(account.UserId, account.Provider, account.Id, account.BankName, failure, FailingClass(failure), ct);
                    break;
                case ConnectionHealthOutcome.GoDormant:
                    // One account failing for days while its connection works: most likely closed, so the notice says so.
                    await RaiseDormantAsync(
                        account.UserId, account.Provider, account.Id, AccountLabel(account), evaluation.Health, othersSyncing: true, ct);
                    break;
            }

            return new BankHealthVerdict(failure, null);
        }

        await RecordOnAccountAsync(account, failure, ct);
        return await ChargeConnectionAsync(connection, stored, failure, at, ct);
    }

    private Task<ConnectionHealthEvaluation?> RecordOnAccountAsync(BankAccount account, ProviderFailure failure, CancellationToken ct) =>
        recorder.RecordFailureAsync(
            AccountSubject(account), account.Health, failure, (health, c) => SaveAccountHealthAsync(account, health, c), ct);

    public async Task RecordConnectionSuccessAsync(BankConnectionRef connection, CancellationToken ct = default)
    {
        var stored = await ReadAsync(connection, ct);
        if (stored is not null)
        {
            var evaluation = await recorder.RecordSuccessAsync(
                ConnectionSubject(connection), stored.Health, (health, c) => StoreOf(connection).SaveHealthUnscopedAsync(connection.Id, health, c), ct);
            if (Healed(evaluation))
                await ResolveDormantAsync(connection.UserId, connection.Provider, connection.Id, ct);
        }

        if (IsActive(connection.Provider))
            await ResolveAsync(connection.UserId, connection.Provider, connection.Id, ct);
    }

    public Task<BankHealthVerdict> RecordConnectionFailureAsync(
        BankConnectionRef connection, Exception exception, CancellationToken ct = default) =>
        RecordConnectionFailureCoreAsync(connection, exception, probe: false, ct);

    public Task<BankHealthVerdict> RecordConnectionProbeFailureAsync(
        BankConnectionRef connection, Exception exception, CancellationToken ct = default) =>
        RecordConnectionFailureCoreAsync(connection, exception, probe: true, ct);

    private async Task<BankHealthVerdict> RecordConnectionFailureCoreAsync(
        BankConnectionRef connection, Exception exception, bool probe, CancellationToken ct)
    {
        var at = time.GetUtcNow();
        var stored = await ReadAsync(connection, ct);
        var failure = Classify(connection.Provider, exception, at, stored);

        return stored is null
            ? new BankHealthVerdict(failure, null)
            : await ChargeConnectionAsync(connection, stored, failure, at, ct, probe);
    }

    public async Task ResolveReplacedConnectionAlertsAsync(
        Guid userId, string provider, IReadOnlyCollection<Guid> replacedConnectionIds, CancellationToken ct = default)
    {
        if (!IsActive(provider))
            return;

        foreach (var connectionId in replacedConnectionIds)
        {
            await ResolveAsync(userId, provider, connectionId, ct);
            await ResolveDormantAsync(userId, provider, connectionId, ct);
        }
    }

    private async Task<BankHealthVerdict> ChargeConnectionAsync(
        BankConnectionRef connection,
        StoredConnectionHealth stored,
        ProviderFailure failure,
        DateTimeOffset at,
        CancellationToken ct,
        bool probe = false)
    {
        if (failure is { Class: FailureClass.Credential, Strength: FailureStrength.Suspect }
            && await IsProviderWideOutageAsync(connection, at, ct))
        {
            failure = ProviderFailure.Transient(failure.Code);
        }

        Func<ConnectionHealth, CancellationToken, Task> persist =
            (health, c) => StoreOf(connection).SaveHealthUnscopedAsync(connection.Id, health, c);
        var evaluation = probe
            ? await recorder.RecordProbeFailureAsync(ConnectionSubject(connection), stored.Health, failure, persist, ct)
            : await recorder.RecordFailureAsync(ConnectionSubject(connection), stored.Health, failure, persist, ct);

        switch (evaluation?.Outcome)
        {
            case ConnectionHealthOutcome.NotifyActionRequired:
                // A confirmed credential loss must reach the user even while the connection's Failing alert is still open
                // or was raised inside the type's silence window: it supersedes that alert instead of being swallowed by it.
                await RaiseAsync(
                    connection.UserId, connection.Provider, connection.Id, connection.Label, failure, SyncFailureClass.Credential, ct,
                    escalate: true);
                break;
            case ConnectionHealthOutcome.NotifyFailing:
                await RaiseAsync(connection.UserId, connection.Provider, connection.Id, connection.Label, failure, FailingClass(failure), ct);
                break;
            case ConnectionHealthOutcome.GoDormant:
                // The one notice that the connection may be closed; the alert raised when it went Failing stays open, and
                // the sync slows (DormantSyncPausedUntilAsync). Resolve fires only on a success.
                await RaiseDormantAsync(
                    connection.UserId, connection.Provider, connection.Id, connection.Label, evaluation.Health, othersSyncing: false, ct);
                break;
        }

        return new BankHealthVerdict(failure, evaluation?.Health ?? stored.Health);
    }

    private static SyncFailureClass FailingClass(ProviderFailure failure) =>
        failure.Class.IsTransientLike() ? SyncFailureClass.Outage : SyncFailureClass.Unknown;

    private ProviderFailure Classify(string provider, Exception exception, DateTimeOffset at, StoredConnectionHealth? stored)
    {
        var context = new ProviderFailureContext(at, stored?.Health ?? new ConnectionHealth());
        return provider switch
        {
            MonobankFailureClassifier.ProviderName => monobankClassifier.Classify(exception, context),
            TrueLayerFailureClassifier.ProviderName => trueLayerClassifier.Classify(exception, context, AsUtc(stored?.ExpiresAt)),
            // Not a bank this policy knows: counted, cause unknown.
            _ => ProviderFailure.Unknown(null),
        };
    }

    /// <summary>
    /// Sibling correlation: the connection succeeded (a sibling account's sync, or discovery's listing) since this
    /// account's current failure streak began, and recently, so the credential works and this failure is the
    /// account's alone. An internal failure is ours and is never re-read. The first failure of a streak is always
    /// charged to the connection: it has no streak start yet, and siblings of the same scheduled run succeed and fail
    /// in no fixed order.
    /// </summary>
    private bool SiblingProvedConnectionWorks(
        ProviderFailure failure, ConnectionHealth connection, ConnectionHealth account, DateTimeOffset at)
    {
        if (failure.Class == FailureClass.Internal)
            return false;

        return connection.LastSuccessAt is { } connectionSuccess
            && account.FirstFailureAt is { } streakStart
            && connectionSuccess >= streakStart
            && at - connectionSuccess <= options.CurrentValue.FailingMinTimeWithoutSuccess;
    }

    /// <summary>
    /// Provider-wide correlation: enough of the provider's other connections (any user) are failing in the same window,
    /// and none of them succeeded in it, so a 401/403 now says more about the provider than about this consent.
    /// </summary>
    private async Task<bool> IsProviderWideOutageAsync(BankConnectionRef connection, DateTimeOffset at, CancellationToken ct)
    {
        try
        {
            var since = at - options.CurrentValue.FailingMinTimeWithoutSuccess;
            var snapshot = await StoreOf(connection).GetProviderHealthUnscopedAsync(connection.Id, since, ct);
            return snapshot.OtherConnectionsFailing >= ProviderWideMinOtherFailingConnections
                && !snapshot.AnyOtherConnectionSucceeded;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Connection health: could not read {Provider}'s other connections; judging {ConnectionId} on its own",
                connection.Provider, connection.Id);
            return false;
        }
    }

    private async Task<StoredConnectionHealth?> ReadAsync(BankConnectionRef connection, CancellationToken ct)
    {
        try
        {
            return await StoreOf(connection).GetHealthUnscopedAsync(connection.Id, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Connection health: could not read {Provider} connection {ConnectionId}",
                connection.Provider, connection.Id);
            return null;
        }
    }

    private async Task RaiseAsync(
        Guid userId, string provider, Guid referenceId, string label, ProviderFailure failure,
        SyncFailureClass failureClass, CancellationToken ct, bool escalate = false)
    {
        // Shadow mode: the verdict is recorded and logged by the recorder; the sync's own alerting stands.
        if (!IsActive(provider))
            return;

        try
        {
            var prefs = await userPreferences.GetAsync(userId, ct);
            if (prefs is null || !prefs.SyncFailureAlerts)
                return;

            if (escalate)
                await alerts.EscalateSyncFailureAlertAsync(userId, provider, referenceId, label, failure.Code, ct);
            else
                await alerts.GenerateSyncFailureAlertAsync(userId, provider, referenceId, label, failure.Code, failureClass, ct);
            logger.LogInformation(
                "Connection health: raised the {SyncFailureClass} sync-failure alert for {Provider} {ReferenceId} of user {UserId}",
                failureClass, provider, referenceId, userId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Connection health: could not raise the sync-failure alert for {Provider} {ReferenceId}", provider, referenceId);
        }
    }

    private async Task RaiseDormantAsync(
        Guid userId, string provider, Guid referenceId, string label, ConnectionHealth health, bool othersSyncing,
        CancellationToken ct)
    {
        if (!IsActive(provider))
            return;

        try
        {
            var prefs = await userPreferences.GetAsync(userId, ct);
            if (prefs is null || !prefs.SyncFailureAlerts)
                return;

            await alerts.GenerateConnectionDormantAlertAsync(
                userId, provider, referenceId, label, ConnectionHealthSyncFailureAlerts.FailingFor(health), othersSyncing, ct);
            logger.LogInformation(
                "Connection health: raised the Dormant notice for {Provider} {ReferenceId} of user {UserId}", provider, referenceId, userId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Connection health: could not raise the Dormant notice for {Provider} {ReferenceId}", provider, referenceId);
        }
    }

    private async Task ResolveDormantAsync(Guid userId, string provider, Guid referenceId, CancellationToken ct)
    {
        if (!IsActive(provider))
            return;

        try
        {
            var prefs = await userPreferences.GetAsync(userId, ct);
            if (prefs is null || !prefs.SyncFailureAlerts)
                return;

            await alerts.ResolveConnectionDormantAlertAsync(userId, provider, referenceId, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Connection health: could not resolve the Dormant notice for {Provider} {ReferenceId}", provider, referenceId);
        }
    }

    // A success that ended a Dormant streak: the Dormant notice, if one went out, is resolved.
    private static bool Healed(ConnectionHealthEvaluation? evaluation) =>
        evaluation is { Previous.State: ConnectionHealthState.Dormant, Health.State: not ConnectionHealthState.Dormant };

    // How the Dormant notice names one account: its bank and the last digits, as the account pickers do.
    private static string AccountLabel(BankAccount account) => $"{account.BankName} ···{account.AccountNumberLast4}";

    private async Task ResolveAsync(Guid userId, string provider, Guid referenceId, CancellationToken ct)
    {
        try
        {
            var prefs = await userPreferences.GetAsync(userId, ct);
            if (prefs is null || !prefs.SyncFailureAlerts)
                return;

            await alerts.ResolveSyncFailureAlertAsync(userId, provider, referenceId, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Connection health: could not resolve the sync-failure alert for {Provider} {ReferenceId}", provider, referenceId);
        }
    }

    private Task SaveAccountHealthAsync(BankAccount account, ConnectionHealth health, CancellationToken ct)
    {
        account.ApplyHealth(health);
        return accounts.SaveHealthUnscopedAsync(account.Id, health, ct);
    }

    private IConnectionHealthStore StoreOf(BankConnectionRef connection) =>
        connection.Provider == MonobankFailureClassifier.ProviderName ? monobankCredentials : trueLayerConnections;

    private static BankConnectionRef? ConnectionOf(BankAccount account) => account.Provider switch
    {
        MonobankFailureClassifier.ProviderName when account.MonobankCredentialId is { } id =>
            new BankConnectionRef(account.Provider, id, account.UserId, account.BankName),
        TrueLayerFailureClassifier.ProviderName when account.TrueLayerConnectionId is { } id =>
            new BankConnectionRef(account.Provider, id, account.UserId, account.BankName),
        _ => null,
    };

    private static ConnectionHealthSubject AccountSubject(BankAccount account) =>
        new(account.Provider, nameof(BankAccount), account.Id, account.UserId);

    private static ConnectionHealthSubject ConnectionSubject(BankConnectionRef connection) =>
        new(
            connection.Provider,
            connection.Provider == MonobankFailureClassifier.ProviderName ? nameof(MonobankCredential) : nameof(TrueLayerConnection),
            connection.Id,
            connection.UserId);

    // Npgsql reads timestamptz as UTC; a value with no kind (the in-memory provider) is stored UTC by convention.
    private static DateTimeOffset? AsUtc(DateTime? value) => value switch
    {
        null => null,
        { Kind: DateTimeKind.Local } local => new DateTimeOffset(local.ToUniversalTime()),
        { } utc => new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)),
    };
}
