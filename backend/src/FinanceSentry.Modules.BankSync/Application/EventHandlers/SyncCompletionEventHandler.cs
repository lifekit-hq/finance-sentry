namespace FinanceSentry.Modules.BankSync.Application.EventHandlers;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.BankSync.Domain.Events;
using FinanceSentry.Modules.BankSync.Domain.Repositories;

/// <summary>
/// Handles <see cref="AccountSyncCompletedEvent"/> published after a sync job finishes.
/// Updates the BankAccount's SyncStatus to reflect the outcome.
/// </summary>
public class SyncCompletionEventHandler(IBankAccountRepository accounts) : IEventHandler<AccountSyncCompletedEvent>
{
    private readonly IBankAccountRepository _accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));

    public async Task Handle(AccountSyncCompletedEvent @event, CancellationToken cancellationToken)
    {
        var account = await _accounts.GetByIdUnscopedAsync(@event.AccountId, cancellationToken);
        if (account == null)
            return;

        switch (@event.Status)
        {
            case "success":
                if (account.SyncStatus == "syncing")
                    account.MarkActive(account.CurrentBalance ?? 0m);
                break;

            case "failed":
                if (account.SyncStatus == "syncing")
                    account.MarkFailed(@event.ErrorMessage);
                break;
        }

        await _accounts.UpdateAsync(account, cancellationToken);
    }
}
