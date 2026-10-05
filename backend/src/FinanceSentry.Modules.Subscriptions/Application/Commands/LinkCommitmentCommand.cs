namespace FinanceSentry.Modules.Subscriptions.Application.Commands;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Subscriptions.Domain;
using FinanceSentry.Modules.Subscriptions.Domain.Exceptions;
using FinanceSentry.Modules.Subscriptions.Domain.Repositories;

/// <summary>
/// Links a legacy hand-typed row to one of the user's transactions: the row is re-keyed to that
/// transaction's charges, which become its last charge, and from then on it follows them like any
/// tracked row. The row keeps the name, kind and plan the user typed.
/// </summary>
public record LinkCommitmentCommand(Guid UserId, Guid Id, Guid TransactionId, string? Cadence = null) : ICommand<bool>;

public class LinkCommitmentCommandHandler(
    IDetectedSubscriptionRepository repository,
    ICommitmentTransactionReader transactions) : ICommandHandler<LinkCommitmentCommand, bool>
{
    public async Task<bool> Handle(LinkCommitmentCommand command, CancellationToken ct)
    {
        if (command.Cadence is not null && !SubscriptionCadences.IsValid(command.Cadence))
            throw new InvalidCommitmentCadenceException();

        var userId = command.UserId.ToString();
        var item = await repository.GetByIdAsync(command.Id, ct);
        if (item is null || item.UserId != userId)
            throw new SubscriptionNotFoundException();
        if (item.IsTracked)
            throw new CommitmentAlreadyLinkedException();

        var transaction = await transactions.FindAsync(command.UserId, command.TransactionId, ct)
            ?? throw new CommitmentTransactionNotFoundException();

        var holder = await repository.FindByUserAndMerchantUnscopedAsync(userId, transaction.Key, ct);
        if (holder is { Status: SubscriptionStatus.Active })
            throw new CommitmentAlreadyTrackedException(holder.MerchantNameDisplay);
        // The key is unique per person; a dismissed, completed or lapsed holder of the same charges
        // is superseded by the row the user is linking.
        if (holder is not null)
            await repository.DeleteAsync(holder, ct);

        item.TrackFromTransaction(
            transaction.Key,
            item.MerchantNameDisplay,
            transaction.Amount,
            transaction.Currency,
            transaction.Date,
            transaction.ChargeCount,
            item.TermCount,
            item.StartDate ?? item.LastChargeDate,
            item.EndDate,
            item.Kind,
            transaction.Cadence ?? command.Cadence ?? item.Cadence);
        await repository.UpsertAsync(item, ct);
        return true;
    }
}
