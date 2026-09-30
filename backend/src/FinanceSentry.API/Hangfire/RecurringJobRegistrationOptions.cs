namespace FinanceSentry.API.Hangfire;

/// <summary>
/// Retry policy for <see cref="RecurringJobRegistrationService"/>. <paramref name="Budget"/> is derived
/// from the configured Hangfire storage's <c>DistributedLockTimeout</c> (see
/// <see cref="RecurringJobRegistrationExtensions"/>), so the two cannot drift apart.
/// </summary>
public sealed record RecurringJobRegistrationOptions(
    TimeSpan Budget,
    TimeSpan InitialDelay,
    TimeSpan MaxDelay);
