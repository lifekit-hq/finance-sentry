namespace FinanceSentry.Modules.Subscriptions.Application.Commands;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Subscriptions.Domain;
using FinanceSentry.Modules.Subscriptions.Domain.Exceptions;
using FinanceSentry.Modules.Subscriptions.Domain.Repositories;

/// <summary>
/// Adds a subscription or installment from one of the user's transactions. The transaction
/// supplies the key, currency and first charge date, so the row is tracked by the same charges
/// the detection job reads; the name, amount and term default to the transaction's but the
/// user may adjust them.
/// </summary>
public record AddCommitmentCommand(
    Guid UserId,
    Guid TransactionId,
    string Kind,
    string? Merchant,
    decimal? MonthlyAmount,
    int? TermCount) : ICommand<Guid>;

public class AddCommitmentCommandHandler(
    IDetectedSubscriptionRepository repository,
    ICommitmentTransactionReader transactions) : ICommandHandler<AddCommitmentCommand, Guid>
{
    public async Task<Guid> Handle(AddCommitmentCommand command, CancellationToken ct)
    {
        if (command.Kind is not (SubscriptionKinds.Subscription or SubscriptionKinds.Installment))
            throw new InvalidCommitmentKindException();

        var transaction = await transactions.FindAsync(command.UserId, command.TransactionId, ct)
            ?? throw new CommitmentTransactionNotFoundException();

        var userId = command.UserId.ToString();
        var display = string.IsNullOrWhiteSpace(command.Merchant) ? transaction.DisplayName : command.Merchant.Trim();
        var amount = command.MonthlyAmount is > 0 ? command.MonthlyAmount.Value : transaction.Amount;

        var existing = await repository.FindByUserAndMerchantUnscopedAsync(userId, transaction.Key, ct);
        if (existing is { Status: SubscriptionStatus.Active } && existing.Kind == command.Kind)
            throw new CommitmentAlreadyTrackedException(existing.MerchantNameDisplay);

        // A dismissed, completed or lapsed row (or a detected one of the other kind) already holds
        // the key, which is unique per person: the pick brings that row back instead of adding a twin.
        var item = existing ?? DetectedSubscription.CreateFromTransaction(
            userId, transaction.Key, display, amount, transaction.Currency, transaction.Date,
            transaction.ChargeCount, command.TermCount, command.Kind);
        existing?.TrackFromTransaction(
            transaction.Key, display, amount, transaction.Currency, transaction.Date,
            transaction.ChargeCount, command.TermCount, transaction.Date, null, command.Kind);

        await repository.UpsertAsync(item, ct);
        return item.Id;
    }
}
