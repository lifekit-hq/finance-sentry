namespace FinanceSentry.Tests.Unit.Connections;

using FinanceSentry.Core.Connections;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.BankSync.Application.Connections;
using FinanceSentry.Modules.BankSync.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

/// <summary>The real bank connection-health policy over the S1 recorder, sharing one clock, for tests that run a sync through it.</summary>
internal static class BankConnectionHealths
{
    public static BankConnectionHealth Create(
        IBankAccountRepository accounts,
        IMonobankCredentialRepository monobankCredentials,
        ITrueLayerConnectionRepository trueLayerConnections,
        IAlertGeneratorService alerts,
        IUserAlertPreferencesReader userPreferences,
        TimeProvider time,
        ConnectionHealthOptions? options = null)
    {
        options ??= new ConnectionHealthOptions();
        return new BankConnectionHealth(
            ShadowRecorders.Create(time, options),
            new MonobankFailureClassifier(),
            new TrueLayerFailureClassifier(),
            monobankCredentials,
            trueLayerConnections,
            accounts,
            alerts,
            userPreferences,
            Mock.Of<IOptionsMonitor<ConnectionHealthOptions>>(m => m.CurrentValue == options),
            time,
            NullLogger<BankConnectionHealth>.Instance);
    }
}

/// <summary>Backs a mocked connection repository's health with one in-memory row, as the real repository does.</summary>
internal static class ConnectionHealthStores
{
    public static void Back<TStore>(
        Mock<TStore> store,
        Guid id,
        Func<ConnectionHealth> read,
        Action<ConnectionHealth> write,
        Func<DateTime?>? expiresAt = null)
        where TStore : class, IConnectionHealthStore
    {
        store.Setup(s => s.GetHealthUnscopedAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new StoredConnectionHealth(read(), expiresAt?.Invoke()));
        store.Setup(s => s.SaveHealthUnscopedAsync(id, It.IsAny<ConnectionHealth>(), It.IsAny<CancellationToken>()))
            .Callback((Guid _, ConnectionHealth health, CancellationToken _) => write(health))
            .Returns(Task.CompletedTask);
        store.Setup(s => s.GetProviderHealthUnscopedAsync(id, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProviderHealthSnapshot(0, false));
    }
}
