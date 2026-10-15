namespace FinanceSentry.Core.Connections;

/// <summary>What kind of failure a provider call ended in (report §5). Drives the policy.</summary>
public enum FailureClass
{
    /// <summary>Provider or network trouble; the credential is presumably fine.</summary>
    Transient,

    /// <summary>The provider refused the credential or consent; see <see cref="FailureStrength"/>.</summary>
    Credential,

    /// <summary>The credential works, but one account or resource keeps failing.</summary>
    ResourceGone,

    /// <summary>Our fault, not the provider's. Never counts toward a user-facing state.</summary>
    Internal,

    /// <summary>Not classified yet. Counts like a non-credential failure.</summary>
    Unknown,

    /// <summary>
    /// The provider asked us to slow down (a 429, a rate-limit code). Counts like <see cref="Transient"/>, is never a
    /// credential signal, and may name the time before which no scheduled call goes out
    /// (<see cref="ProviderFailure.NotBefore"/>). Appended: the class is stored as text, so no row changes.
    /// </summary>
    RateLimited,

    /// <summary>
    /// The provider has not produced what we asked for yet (a statement still being generated). Not counted until the
    /// last success is older than the source's <see cref="ConnectionHealthSourceOptions.NotReadyDeadline"/>, then it
    /// counts like <see cref="Transient"/>. Appended, like <see cref="RateLimited"/>.
    /// </summary>
    NotReady,
}

public static class FailureClassExtensions
{
    /// <summary>
    /// Whether the class reads as a provider-side outage that clears on its own: what the outage copy of an alert and
    /// an account's "transient retry" status are for. <see cref="FailureClass.RateLimited"/> and
    /// <see cref="FailureClass.NotReady"/> are a kind of transient failure, so they are told the way one is.
    /// </summary>
    public static bool IsTransientLike(this FailureClass failureClass) =>
        failureClass is FailureClass.Transient or FailureClass.RateLimited or FailureClass.NotReady;
}
