namespace FinanceSentry.Modules.BankSync.Application.Connections;

using FinanceSentry.Core.Connections;
using FinanceSentry.Modules.BankSync.Infrastructure.Monobank;

/// <summary>
/// Reads a failed Monobank call as a <see cref="ProviderFailure"/> (connection health, Option B, S2).
/// Monobank answers 403 for a dead token but also for reasons that clear on their own, so a rejected
/// token is only a suspect credential failure: the policy confirms it when it is seen again after
/// <see cref="ConnectionHealthOptions.SuspectConfirmationWindow"/>.
/// </summary>
public sealed class MonobankFailureClassifier : IProviderFailureClassifier
{
    public const string ProviderName = "monobank";

    /// <summary>The code a network or timeout failure is recorded with; the sync's own result carries it too.</summary>
    public const string UnavailableCode = "MONOBANK_UNAVAILABLE";

    private const int ServerErrorStatus = 500;

    public string Provider => ProviderName;

    public ProviderFailure Classify(Exception exception, ProviderFailureContext context)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is MonobankException mono)
        {
            return mono.ErrorCode switch
            {
                "MONOBANK_TOKEN_INVALID" => ProviderFailure.CredentialSuspect(mono.ErrorCode),
                "MONOBANK_RATE_LIMITED" => ProviderFailure.Transient(mono.ErrorCode),
                "MONOBANK_PARSE_ERROR" => ProviderFailure.Internal(mono.ErrorCode),
                _ when mono.StatusCode >= ServerErrorStatus => ProviderFailure.Transient(mono.ErrorCode),
                _ => ProviderFailure.Unknown(mono.ErrorCode),
            };
        }

        return BankFailureShapes.ClassifyNonProvider(exception, UnavailableCode);
    }
}
