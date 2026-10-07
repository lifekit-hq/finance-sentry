namespace FinanceSentry.Core.Connections;

/// <summary>One failed provider attempt, as a classifier reads it: a class, how sure it is, and the provider's code.</summary>
public sealed record ProviderFailure(FailureClass Class, FailureStrength Strength, string? Code)
{
    public static ProviderFailure Transient(string? code) => new(FailureClass.Transient, FailureStrength.Definitive, code);

    public static ProviderFailure CredentialDefinitive(string? code) => new(FailureClass.Credential, FailureStrength.Definitive, code);

    public static ProviderFailure CredentialSuspect(string? code) => new(FailureClass.Credential, FailureStrength.Suspect, code);

    public static ProviderFailure ResourceGone(string? code) => new(FailureClass.ResourceGone, FailureStrength.Definitive, code);

    public static ProviderFailure Internal(string? code) => new(FailureClass.Internal, FailureStrength.Definitive, code);

    public static ProviderFailure Unknown(string? code) => new(FailureClass.Unknown, FailureStrength.Definitive, code);
}
