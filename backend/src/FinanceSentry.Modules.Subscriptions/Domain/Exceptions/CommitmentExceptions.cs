namespace FinanceSentry.Modules.Subscriptions.Domain.Exceptions;

using FinanceSentry.Core.Exceptions;

public class CommitmentTransactionNotFoundException()
    : ApiException(404, "COMMITMENT_TRANSACTION_NOT_FOUND", "Transaction not found.");

public class CommitmentAlreadyTrackedException(string trackedAs)
    : ApiException(409, "COMMITMENT_ALREADY_TRACKED", $"These charges are already tracked as {trackedAs}.");

public class CommitmentAlreadyLinkedException()
    : ApiException(409, "COMMITMENT_ALREADY_LINKED", "This row already follows its transactions.");

public class InvalidCommitmentKindException()
    : ApiException(400, "INVALID_COMMITMENT_KIND", "Kind must be subscription or installment.");
