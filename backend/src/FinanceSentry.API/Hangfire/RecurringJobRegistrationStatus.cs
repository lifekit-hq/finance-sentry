namespace FinanceSentry.API.Hangfire;

public enum RecurringJobRegistrationState
{
    /// <summary>Registration has not completed yet; the service is still inside its retry budget.</summary>
    Pending,

    /// <summary>Every module job and the FX refresh job are registered.</summary>
    Registered,

    /// <summary>Registration was deliberately not attempted (database unreachable at startup); the <c>migrations</c> check names it.</summary>
    Skipped,

    /// <summary>Lock timeouts outlasted the whole retry budget; this build's schedule is not registered.</summary>
    Failed,
}

/// <summary>
/// Progress of <see cref="RecurringJobRegistrationService"/>, read by the <c>job-registration</c>
/// readiness check.
/// </summary>
public sealed class RecurringJobRegistrationStatus
{
    private volatile RecurringJobRegistrationState _state = RecurringJobRegistrationState.Pending;

    public RecurringJobRegistrationState State => _state;

    public void Set(RecurringJobRegistrationState state) => _state = state;
}
