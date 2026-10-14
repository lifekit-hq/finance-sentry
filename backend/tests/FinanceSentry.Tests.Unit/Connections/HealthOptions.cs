namespace FinanceSentry.Tests.Unit.Connections;

using FinanceSentry.Core.Connections;
using Microsoft.Extensions.Options;
using Moq;

/// <summary>Connection-health options for a job under test, with the given providers in <see cref="ConnectionHealthOptions.ActiveProviders"/>.</summary>
internal static class HealthOptions
{
    public static ConnectionHealthOptions With(params string[] activeProviders) =>
        new() { ActiveProviders = [.. activeProviders] };

    public static IOptionsMonitor<ConnectionHealthOptions> Monitor(ConnectionHealthOptions options) =>
        Mock.Of<IOptionsMonitor<ConnectionHealthOptions>>(m => m.CurrentValue == options);
}
