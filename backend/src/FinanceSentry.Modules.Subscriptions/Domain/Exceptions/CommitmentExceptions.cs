namespace FinanceSentry.Modules.Subscriptions.Domain.Exceptions;

using FinanceSentry.Core.Exceptions;

public class CommitmentTransactionNotFoundException()
    : ApiException(404, "COMMITMENT_TRANSACTION_NOT_FOUND", "Transaction not found.");

public class CommitmentAlreadyTrackedException()
    : ApiException(409, "COMMITMENT_ALREADY_TRACKED", "These charges are already tracked.");

public class InvalidCommitmentKindException()
    : ApiException(400, "INVALID_COMMITMENT_KIND", "Kind must be subscription or installment.");
