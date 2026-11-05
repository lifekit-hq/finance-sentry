namespace FinanceSentry.Modules.Agent.Tests;

using System.Reflection;
using System.Text.Json;
using FinanceSentry.Core.Auditing;
using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.Agent.Application.Services;
using FinanceSentry.Modules.Agent.Tests.Support;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

/// <summary>
/// The in-app agent reads through the same tools as an MCP client. A tool marked <c>[SensitiveProfile]</c> is recorded
/// on the audit trail as the agent's read BEFORE it runs, with ids and field names only; a read that cannot be
/// recorded is refused with the tool never run. The bridge audits by the mark - the tool remembers nothing.
/// </summary>
public sealed class McpToolBridgeAuditTests
{
    private static readonly Assembly TestAssembly = typeof(FakeEchoTool).Assembly;
    private static readonly JsonElement NoInput = JsonSerializer.SerializeToElement(new { });

    private static McpToolBridge Bridge() =>
        new(Options.Create(new AgentOptions()), NullLogger<McpToolBridge>.Instance, TestAssembly);

    private static ServiceProvider Scope(Guid person, RecordingTrail trail, FakeProfileToolProbe probe)
        => new ServiceCollection()
            .AddScoped<IFakeIdentity>(_ => new FakeIdentity(person))
            .AddSingleton(probe)
            .AddScoped<FakeProfileTool>()
            .AddScoped<FakeEchoTool>()
            .AddSingleton<IAuditTrail>(trail)
            // The API host's default actor: the signed-in person.
            .AddScoped<IAuditActor>(_ => new AuditActor(new FixedUser(person), () => AuditActorKind.OwnerSession))
            .BuildServiceProvider();

    [Fact]
    public async Task AMarkedTool_IsRecordedAsTheAgentsRead_BeforeItRuns()
    {
        var person = Guid.NewGuid();
        var probe = new FakeProfileToolProbe();
        var trail = new RecordingTrail { OnRecord = () => probe.Runs.Should().Be(0, "the record is written before the tool runs") };
        using var scope = Scope(person, trail, probe);

        var result = await Bridge().DispatchAsync("tu_1", "fake_profile", NoInput, scope, CancellationToken.None);

        result.IsError.Should().BeFalse();
        probe.Runs.Should().Be(1);
        var entry = trail.Entries.Should().ContainSingle().Subject;
        entry.Action.Should().Be(AuditAction.Read);
        entry.ActorKind.Should().Be(AuditActorKind.InAppAgent);
        entry.ResourceType.Should().Be("fake_profile");
        entry.SubjectUserId.Should().Be(person);
        entry.ActorUserId.Should().Be(person);
        entry.FieldNames.Should().Equal("Amount", "Cadence");
    }

    [Fact]
    public async Task WhenTheTrailCannotBeWritten_TheToolDoesNotRun_AndTheModelIsToldItWasRefused()
    {
        var probe = new FakeProfileToolProbe();
        var trail = new RecordingTrail { Failure = new InvalidOperationException("connection string: Password=hunter2") };
        using var scope = Scope(Guid.NewGuid(), trail, probe);

        var result = await Bridge().DispatchAsync("tu_2", "fake_profile", NoInput, scope, CancellationToken.None);

        result.IsError.Should().BeTrue();
        probe.Runs.Should().Be(0);
        result.Content.Should().Contain("audit trail").And.NotContain("hunter2");
    }

    [Fact]
    public async Task AnUnmarkedTool_IsNotRecorded()
    {
        var trail = new RecordingTrail();
        using var scope = Scope(Guid.NewGuid(), trail, new FakeProfileToolProbe());

        var input = JsonSerializer.SerializeToElement(new { message = "hi" });
        var result = await Bridge().DispatchAsync("tu_3", "fake_echo", input, scope, CancellationToken.None);

        result.IsError.Should().BeFalse();
        trail.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task TheAgentActsAsTheAgentOnlyForTheSpanOfTheCall()
    {
        var person = Guid.NewGuid();
        using var scope = Scope(person, new RecordingTrail(), new FakeProfileToolProbe());
        var actor = scope.GetRequiredService<IAuditActor>();

        await Bridge().DispatchAsync("tu_4", "fake_profile", NoInput, scope, CancellationToken.None);

        actor.Kind.Should().Be(AuditActorKind.OwnerSession, "the request's own actor is restored once the tool call ends");
    }

    [Fact]
    public async Task ADispatchWithNoAuditServices_StillServesAnUnmarkedTool()
    {
        using var scope = new ServiceCollection()
            .AddScoped<IFakeIdentity>(_ => new FakeIdentity(Guid.NewGuid()))
            .AddScoped<FakeEchoTool>()
            .BuildServiceProvider();

        var input = JsonSerializer.SerializeToElement(new { message = "hi" });
        var result = await Bridge().DispatchAsync("tu_5", "fake_echo", input, scope, CancellationToken.None);

        result.IsError.Should().BeFalse();
    }

    [Fact]
    public async Task AMarkedTool_WithNoTrailRegistered_IsRefused()
    {
        var probe = new FakeProfileToolProbe();
        using var scope = new ServiceCollection()
            .AddScoped<IFakeIdentity>(_ => new FakeIdentity(Guid.NewGuid()))
            .AddSingleton(probe)
            .AddScoped<FakeProfileTool>()
            .AddScoped<IAuditActor>(_ => new AuditActor(new FixedUser(Guid.NewGuid()), () => AuditActorKind.OwnerSession))
            .BuildServiceProvider();

        var result = await Bridge().DispatchAsync("tu_6", "fake_profile", NoInput, scope, CancellationToken.None);

        result.IsError.Should().BeTrue("a sensitive read is never served when the trail is missing");
        probe.Runs.Should().Be(0);
    }

    private sealed class FixedUser(Guid? userId) : ICurrentUser
    {
        public Guid? UserId { get; } = userId;
    }

    private sealed class RecordingTrail : IAuditTrail
    {
        public List<AuditEntry> Entries { get; } = [];
        public Exception? Failure { get; init; }
        public Action? OnRecord { get; init; }

        public Task RecordAsync(IReadOnlyCollection<AuditEntry> entries, CancellationToken cancellationToken = default)
        {
            if (Failure is not null)
                throw Failure;

            OnRecord?.Invoke();
            Entries.AddRange(entries);
            return Task.CompletedTask;
        }
    }
}
