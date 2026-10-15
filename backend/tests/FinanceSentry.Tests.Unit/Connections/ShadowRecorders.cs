namespace FinanceSentry.Tests.Unit.Connections;

using FinanceSentry.Core.Connections;
using FinanceSentry.Infrastructure.Connections;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

/// <summary>The real shadow recorder with default thresholds, for tests that run a sync through it.</summary>
internal static class ShadowRecorders
{
    public static ConnectionHealthShadowRecorder Create(
        TimeProvider? time = null,
        ConnectionHealthOptions? options = null,
        ILogger<ConnectionHealthShadowRecorder>? logger = null,
        IReadOnlyDictionary<string, ConnectionHealthSourceOptions>? sources = null) =>
        new(
            Mock.Of<IOptionsMonitor<ConnectionHealthOptions>>(m => m.CurrentValue == (options ?? new ConnectionHealthOptions())),
            SourceOptions(sources),
            time ?? TimeProvider.System,
            logger ?? NullLogger<ConnectionHealthShadowRecorder>.Instance);

    /// <summary>Per-source options by provider name; a provider not listed gets the defaults, as the named-options monitor does.</summary>
    public static IOptionsMonitor<ConnectionHealthSourceOptions> SourceOptions(
        IReadOnlyDictionary<string, ConnectionHealthSourceOptions>? sources = null)
    {
        var monitor = new Mock<IOptionsMonitor<ConnectionHealthSourceOptions>>();
        monitor
            .Setup(m => m.Get(It.IsAny<string?>()))
            .Returns((string? name) =>
                name is not null && sources is not null && sources.TryGetValue(name, out var source)
                    ? source
                    : new ConnectionHealthSourceOptions());
        return monitor.Object;
    }
}
