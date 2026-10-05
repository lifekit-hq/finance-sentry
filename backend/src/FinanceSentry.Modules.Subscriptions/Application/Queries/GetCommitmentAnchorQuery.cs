namespace FinanceSentry.Modules.Subscriptions.Application.Queries;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Subscriptions.API.Responses;
using FinanceSentry.Modules.Subscriptions.Domain.Exceptions;

public record GetCommitmentAnchorQuery(Guid UserId, Guid TransactionId) : IQuery<CommitmentAnchorResponse>;

public class GetCommitmentAnchorQueryHandler(ICommitmentTransactionReader transactions)
    : IQueryHandler<GetCommitmentAnchorQuery, CommitmentAnchorResponse>
{
    public async Task<CommitmentAnchorResponse> Handle(GetCommitmentAnchorQuery request, CancellationToken cancellationToken)
    {
        var transaction = await transactions.FindAsync(request.UserId, request.TransactionId, cancellationToken)
            ?? throw new CommitmentTransactionNotFoundException();

        return new CommitmentAnchorResponse(
            transaction.Amount, transaction.Currency, transaction.Date, transaction.ChargeCount, transaction.Cadence);
    }
}
