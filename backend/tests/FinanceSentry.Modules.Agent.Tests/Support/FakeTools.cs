namespace FinanceSentry.Modules.Agent.Tests.Support;

using System.ComponentModel;
using FinanceSentry.Mcp.Auditing;
using ModelContextProtocol.Server;

/// <summary>A stand-in identity the fake tool reads its user id from (mirrors <c>IIdentityResolver</c>).</summary>
public interface IFakeIdentity
{
    Guid UserId { get; }
}

public sealed class FakeIdentity(Guid userId) : IFakeIdentity
{
    public Guid UserId { get; } = userId;
}

/// <summary>
/// A minimal user-scoped MCP tool used to test the bridge in isolation. It resolves the caller's user id
/// from the injected identity — never from the <c>userId</c> parameter — exactly like the real tools.
/// </summary>
[McpServerToolType]
public sealed class FakeEchoTool(IFakeIdentity identity)
{
    [McpServerTool(Name = "fake_echo")]
    [Description("Echoes the caller's resolved user id and a message.")]
    public Task<FakeEchoResult> ExecuteAsync(
        [Description("A message to echo back.")] string message,
        [Description("Optional user GUID. Defaults to the authenticated identity.")] Guid? userId = null,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new FakeEchoResult(identity.UserId, message, userId));
}

public sealed record FakeEchoResult(Guid ResolvedUserId, string Message, Guid? UserIdArgument);

/// <summary>A tool that always throws — used to prove graceful degradation (is_error, never fabricate).</summary>
[McpServerToolType]
public sealed class FakeThrowingTool
{
    [McpServerTool(Name = "fake_throw")]
    [Description("Always throws.")]
    public Task<string> ExecuteAsync(CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("boom");
}

/// <summary>
/// A tool that returns declared profile data: marked <c>[SensitiveProfile]</c>, so the bridge must put the call on the
/// audit trail before it runs. It counts its runs so a test can tell a refused call from a served one.
/// </summary>
[McpServerToolType]
public sealed class FakeProfileTool(IFakeIdentity identity, FakeProfileToolProbe probe)
{
    [McpServerTool(Name = "fake_profile")]
    [SensitiveProfile("Amount", "Cadence")]
    [Description("Returns declared profile data.")]
    public Task<string> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        probe.Runs++;
        return Task.FromResult($"profile of {identity.UserId}");
    }
}

public sealed class FakeProfileToolProbe
{
    public int Runs { get; set; }
}
