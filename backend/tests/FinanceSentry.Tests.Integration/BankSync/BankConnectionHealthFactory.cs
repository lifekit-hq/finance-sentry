namespace FinanceSentry.Tests.Integration.BankSync;

using FinanceSentry.Core.Connections;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Infrastructure.Connections;
using FinanceSentry.Modules.BankSync.Application.Connections;
using FinanceSentry.Modules.BankSync.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

/// <summary>
/// The real bank connection-health policy over the real S1 recorder, as the DI container wires it. Every provider is in
/// shadow mode unless named in <c>activeProviders</c> (<c>ConnectionHealth:ActiveProviders</c>).
/// </summary>
internal static class BankConnectionHealthFactory
{
    public static BankConnectionHealth Create(
        IBankAccountRepository accounts,
        IMonobankCredentialRepository monobankCredentials,
        ITrueLayerConnectionRepository trueLayerConnections,
        IAlertGeneratorService alerts,
        IUserAlertPreferencesReader userPreferences,
        TimeProvider? time = null,
        params string[] activeProviders)
    {
        time ??= TimeProvider.System;
        var options = Mock.Of<IOptionsMonitor<ConnectionHealthOptions>>(
            m => m.CurrentValue == new ConnectionHealthOptions { ActiveProviders = activeProviders.ToList() });
        return new BankConnectionHealth(
            new ConnectionHealthShadowRecorder(options, time, NullLogger<ConnectionHealthShadowRecorder>.Instance),
            new MonobankFailureClassifier(),
            new TrueLayerFailureClassifier(),
            monobankCredentials,
            trueLayerConnections,
            accounts,
            alerts,
            userPreferences,
            options,
            time,
            NullLogger<BankConnectionHealth>.Instance);
    }
}
