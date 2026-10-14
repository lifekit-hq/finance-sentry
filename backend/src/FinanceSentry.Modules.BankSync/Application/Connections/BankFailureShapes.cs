namespace FinanceSentry.Modules.BankSync.Application.Connections;

using FinanceSentry.Core.Connections;
using FinanceSentry.Infrastructure.Observability.Hangfire;

/// <summary>The failure shapes Monobank and TrueLayer share: what is not one of the provider's own errors.</summary>
internal static class BankFailureShapes
{
    /// <summary>
    /// A network, timeout or throttling failure on the way to the provider is transient. A stored credential we cannot
    /// decrypt is a definitive credential loss: only the user entering it again restores the connection. Anything
    /// else is unknown, so it still counts towards telling the user: a persistent failure of ours must not go silent.
    /// </summary>
    public static ProviderFailure ClassifyNonProvider(Exception exception, string unavailableCode)
    {
        if (exception is CredentialUnreadableException)
            return ProviderFailure.CredentialDefinitive(CredentialUnreadableException.Code);

        return JobFailureTransientClassifier.IsTransient(exception)
            ? ProviderFailure.Transient(unavailableCode)
            : ProviderFailure.Unknown(null);
    }
}
