namespace FinanceSentry.Modules.BankSync.Application.Commands;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.BankSync.Domain.Repositories;
using FinanceSentry.Modules.BankSync.Infrastructure.Jobs;
using Hangfire;

/// <summary>
/// Disconnects one banking institution (Monobank credential or TrueLayer
/// connection) and cascades to every child sub-account, transaction, and alert.
/// </summary>
public sealed record DisconnectInstitutionCommand(
    Guid UserId,
    string Provider,
    Guid InstitutionId) : ICommand<DisconnectInstitutionResult>;

public sealed record DisconnectInstitutionResult(int RemovedAccounts);

public sealed class DisconnectInstitutionCommandHandler(
    IBankAccountRepository accounts,
    IMonobankCredentialRepository monobankCredentials,
    ITrueLayerConnectionRepository trueLayerConnections,
    IAlertGeneratorService alerts,
    IRecurringJobManager recurringJobs)
    : ICommandHandler<DisconnectInstitutionCommand, DisconnectInstitutionResult>
{
    public async Task<DisconnectInstitutionResult> Handle(DisconnectInstitutionCommand command, CancellationToken ct)
    {
        var provider = command.Provider.ToLowerInvariant();

        var childAccounts = await ResolveChildAccountsAsync(provider, command.UserId, command.InstitutionId, ct);
        if (childAccounts.Count == 0 && !await ParentExistsAsync(provider, command.UserId, command.InstitutionId, ct))
        {
            throw new InvalidOperationException($"No {provider} institution found for the given id.");
        }

        foreach (var account in childAccounts)
        {
            await alerts.DeleteAlertsForAccountAsync(account.Id, ct);
            await accounts.HardDeleteAsync(account.Id, ct);
            recurringJobs.RemoveIfExists(SyncAccountJob.IdFor(account.Id));
        }

        switch (provider)
        {
            case "monobank":
                await monobankCredentials.DeleteAsync(command.InstitutionId, ct);
                await monobankCredentials.SaveChangesAsync(ct);
                break;
            case "truelayer":
                await trueLayerConnections.DeleteAsync(command.InstitutionId, ct);
                break;
            default:
                throw new InvalidOperationException(
                    $"Provider '{command.Provider}' is not routed through the banking institution disconnect handler.");
        }

        return new DisconnectInstitutionResult(childAccounts.Count);
    }

    private async Task<IReadOnlyList<Domain.BankAccount>> ResolveChildAccountsAsync(
        string provider, Guid userId, Guid institutionId, CancellationToken ct)
    {
        var all = await accounts.GetByUserIdAsync(userId, ct);
        return provider switch
        {
            "monobank" => [.. all.Where(a => a.MonobankCredentialId == institutionId)],
            "truelayer" => [.. all.Where(a => a.TrueLayerConnectionId == institutionId)],
            _ => [],
        };
    }

    private async Task<bool> ParentExistsAsync(string provider, Guid userId, Guid institutionId, CancellationToken ct)
    {
        return provider switch
        {
            "monobank" => await monobankCredentials.GetByIdAsync(institutionId, ct) is { } m && m.UserId == userId,
            "truelayer" => await trueLayerConnections.GetByIdAsync(institutionId, ct) is { } t && t.UserId == userId,
            _ => false,
        };
    }
}
