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
        ILogger<ConnectionHealthShadowRecorder>? logger = null) =>
        new(
            Mock.Of<IOptionsMonitor<ConnectionHealthOptions>>(m => m.CurrentValue == (options ?? new ConnectionHealthOptions())),
            time ?? TimeProvider.System,
            logger ?? NullLogger<ConnectionHealthShadowRecorder>.Instance);
}
