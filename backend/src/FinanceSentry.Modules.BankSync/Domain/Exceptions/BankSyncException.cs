using FinanceSentry.Core.Exceptions;

namespace FinanceSentry.Modules.BankSync.Domain.Exceptions;

/// <summary>
/// Base exception for all bank sync domain errors.
/// </summary>
public class BankSyncException : ApiException
{
    public int HttpStatusCode => StatusCode;

    public BankSyncException(string errorCode, string message, int httpStatusCode = 500)
        : base(httpStatusCode, errorCode, message)
    {
    }

    public BankSyncException(string errorCode, string message, Exception inner, int httpStatusCode = 500)
        : base(httpStatusCode, errorCode, message, inner)
    {
    }
}

public class AccountNotFoundException(Guid accountId)
    : BankSyncException("ACCOUNT_NOT_FOUND", $"Account {accountId} not found.", 404);

public class SyncAlreadyRunningException()
    : BankSyncException("SYNC_ALREADY_RUNNING", "A sync is already in progress for this account.", 409);

public class CredentialExpiredException()
    : BankSyncException("CREDENTIAL_EXPIRED", "Bank credentials expired. Please reconnect your account.", 401);

/// <summary>
/// The merchant offered for a committed pin normalizes to nothing nameable. Rejected rather
/// than stored: <c>MerchantNameNormalizer</c> collapses blank and punctuation-only input to the
/// key <c>unknown</c>, which every unnamed debit also carries — a pin on it would silently claim
/// the whole tail of the book as committed. Reuses <c>VALIDATION_ERROR</c> so no new error code
/// has to be threaded into the frontend registry for a surface the app does not render yet.
/// </summary>
public class UnpinnableMerchantException()
    : BankSyncException("VALIDATION_ERROR", "A committed pin needs a recognisable merchant name.", 400);
