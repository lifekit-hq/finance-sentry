namespace FinanceSentry.Modules.BankSync.Application.Connections;

using FinanceSentry.Core.Connections;
using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Domain.Repositories;

/// <summary>
/// The bank module's stored health for the read surfaces (<see cref="IConnectionHealthReader"/>): each Monobank token
/// and TrueLayer consent, and each of the person's accounts with its own sub-state, tied to its connection.
/// </summary>
public sealed class BankConnectionHealthReader(
    IMonobankCredentialRepository monobankCredentials,
    ITrueLayerConnectionRepository trueLayerConnections,
    IBankAccountRepository accounts) : IConnectionHealthReader
{
    public async Task<IReadOnlyList<ConnectionHealthEntry>> GetForUserAsync(Guid userId, CancellationToken ct = default)
    {
        var monobank = await monobankCredentials.GetHealthByUserUnscopedAsync(userId, ct);
        var trueLayer = await trueLayerConnections.GetHealthByUserUnscopedAsync(userId, ct);
        var owned = await accounts.GetByUserIdUnscopedAsync(userId, ct);

        return
        [
            .. monobank.Select(c => new ConnectionHealthEntry(
                MonobankFailureClassifier.ProviderName, nameof(MonobankCredential), c.Id, c.Health)),
            .. trueLayer.Select(c => new ConnectionHealthEntry(
                TrueLayerFailureClassifier.ProviderName, nameof(TrueLayerConnection), c.Id, c.Health)),
            .. owned.Select(a => new ConnectionHealthEntry(
                a.Provider, nameof(BankAccount), a.Id, a.Health, IsAccount: true,
                ConnectionId: a.MonobankCredentialId ?? a.TrueLayerConnectionId)),
        ];
    }
}
