namespace FinanceSentry.Core.Connections;

/// <summary>One failed provider attempt, as a classifier reads it: a class, how sure it is, and the provider's code.</summary>
/// <param name="NotBefore">
/// The earliest time the provider wants to hear from this connection again (a <c>Retry-After</c>, or a time it
/// named), when it said. The policy stores it on <see cref="ConnectionHealth.NotBefore"/>.
/// </param>
public sealed record ProviderFailure(
    FailureClass Class, FailureStrength Strength, string? Code, DateTimeOffset? NotBefore = null)
{
    public static ProviderFailure Transient(string? code) => new(FailureClass.Transient, FailureStrength.Definitive, code);

    public static ProviderFailure CredentialDefinitive(string? code) => new(FailureClass.Credential, FailureStrength.Definitive, code);

    public static ProviderFailure CredentialSuspect(string? code) => new(FailureClass.Credential, FailureStrength.Suspect, code);

    public static ProviderFailure ResourceGone(string? code) => new(FailureClass.ResourceGone, FailureStrength.Definitive, code);

    public static ProviderFailure Internal(string? code) => new(FailureClass.Internal, FailureStrength.Definitive, code);

    public static ProviderFailure Unknown(string? code) => new(FailureClass.Unknown, FailureStrength.Definitive, code);

    /// <param name="code">The provider's code.</param>
    /// <param name="notBefore">The time the provider named, when it named one; null leaves no scheduled-call pause.</param>
    public static ProviderFailure RateLimited(string? code, DateTimeOffset? notBefore = null) =>
        new(FailureClass.RateLimited, FailureStrength.Definitive, code, notBefore);

    public static ProviderFailure NotReady(string? code) => new(FailureClass.NotReady, FailureStrength.Definitive, code);
}
