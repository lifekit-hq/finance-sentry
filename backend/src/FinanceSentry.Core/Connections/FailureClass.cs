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
}
