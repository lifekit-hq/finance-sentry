using System.ComponentModel;
using System.Security.Claims;
using FinanceSentry.Core.Auditing;
using FinanceSentry.Core.Auth;
using FinanceSentry.Mcp.Auditing;
using FinanceSentry.Mcp.Middleware;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Xunit;
using McpTokens = FinanceSentry.Mcp.Authentication.McpAuthenticationExtensions;

namespace FinanceSentry.Mcp.Tests;

/// <summary>
/// A tool marked <see cref="SensitiveProfileAttribute"/> is put on the audit trail before it runs, by the host's
/// call-tool filter, with the tool's name, whose data it was, who asked and the field names it can disclose - never a
/// value. A tool that is not marked is not recorded, and a read that cannot be recorded is refused.
/// </summary>
public sealed class SensitiveProfileReadAuditTests
{
    private const string MarkedTool = "fake_get_profile";
    private const string UnmarkedTool = "fake_get_price";

    private static readonly Guid Person = Guid.NewGuid();

    private static McpRequestFilter<CallToolRequestParams, CallToolResult> Filter()
    {
        var services = new ServiceCollection();
        services.AddMcpServer().WithSensitiveReadAudit(typeof(FakeProfileTools).Assembly);

        return services.BuildServiceProvider()
            .GetRequiredService<IOptions<McpServerOptions>>()
            .Value.Filters.Request.CallToolFilters.Should().ContainSingle().Subject;
    }

    private static ServiceProvider Services(RecordingTrail trail, Guid? person, AuditActorKind kind = AuditActorKind.McpPersonal)
        => new ServiceCollection()
            .AddSingleton<IAuditTrail>(trail)
            .AddScoped<IAuditActor>(_ => new AuditActor(new FixedUser(person), () => kind))
            .BuildServiceProvider();

    private static async Task<(bool Ran, Exception? Failure)> CallAsync(string tool, IServiceProvider services)
    {
        var ran = false;
        var pipeline = Filter()((_, _) =>
        {
            ran = true;
            return ValueTask.FromResult(new CallToolResult());
        });

        var failure = await Record.ExceptionAsync(() => pipeline(TestRequestContext.ForToolCall(tool, services), default).AsTask());
        return (ran, failure);
    }

    [Fact]
    public async Task AMarkedTool_IsRecorded_BeforeItRuns()
    {
        var trail = new RecordingTrail();
        await using var services = Services(trail, Person, AuditActorKind.McpService);

        var (ran, failure) = await CallAsync(MarkedTool, services);

        failure.Should().BeNull();
        ran.Should().BeTrue();
        var entry = trail.Entries.Should().ContainSingle().Subject;
        entry.Action.Should().Be(AuditAction.Read);
        entry.ResourceType.Should().Be(MarkedTool);
        entry.SubjectUserId.Should().Be(Person);
        entry.ActorUserId.Should().Be(Person);
        entry.ActorKind.Should().Be(AuditActorKind.McpService);
        entry.ResourceId.Should().BeNull();
        entry.FieldNames.Should().Equal("Amount", "Cadence");
    }

    [Fact]
    public async Task TheRecordIsWritten_BeforeTheToolIsAllowedToRun()
    {
        var trail = new RecordingTrail();
        await using var services = Services(trail, Person);
        var order = new List<string>();
        trail.OnRecord = () => order.Add("recorded");
        var pipeline = Filter()((_, _) =>
        {
            order.Add("ran");
            return ValueTask.FromResult(new CallToolResult());
        });

        await pipeline(TestRequestContext.ForToolCall(MarkedTool, services), default);

        order.Should().Equal("recorded", "ran");
    }

    [Fact]
    public async Task AnUnmarkedTool_IsNotRecorded_AndRuns()
    {
        var trail = new RecordingTrail();
        await using var services = Services(trail, Person);

        var (ran, failure) = await CallAsync(UnmarkedTool, services);

        failure.Should().BeNull();
        ran.Should().BeTrue();
        trail.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task AMarkedToolOnAClassWithTheAttribute_IsRecordedToo()
    {
        var trail = new RecordingTrail();
        await using var services = Services(trail, Person);

        await CallAsync("fake_class_marked", services);

        trail.Entries.Should().ContainSingle().Which.FieldNames.Should().Equal("Label");
    }

    [Fact]
    public async Task WhenTheTrailCannotBeWritten_TheCallIsRefused_AndTheToolNeverRuns()
    {
        var trail = new RecordingTrail { Failure = new InvalidOperationException("password=hunter2 audit store is down") };
        await using var services = Services(trail, Person);

        var (ran, failure) = await CallAsync(MarkedTool, services);

        ran.Should().BeFalse("a sensitive read with no trail entry must not happen");
        failure.Should().BeOfType<McpException>();
        failure!.Message.Should().Contain(MarkedTool).And.Contain("audit trail")
            .And.NotContain("hunter2", "the caller is told the read was refused, not the internal cause");
    }

    [Fact]
    public async Task WithNoPersonInScope_NothingIsRecorded_BecauseNothingCanBeRead()
    {
        var trail = new RecordingTrail();
        await using var services = Services(trail, person: null);

        var (ran, failure) = await CallAsync(MarkedTool, services);

        failure.Should().BeNull();
        ran.Should().BeTrue();
        trail.Entries.Should().BeEmpty();
    }

    [Fact]
    public void TheHostsToolRegistration_InstallsTheAuditFilter()
    {
        var services = new ServiceCollection();
        services.AddMcpServer().WithFinanceSentryTools(McpServiceRegistration.McpAssembly);

        services.BuildServiceProvider().GetRequiredService<IOptions<McpServerOptions>>()
            .Value.Filters.Request.CallToolFilters.Should().HaveCount(2, "the error-surfacing filter and the sensitive-read audit");
    }

    [Fact]
    public void EverySensitiveToolInTheHost_DeclaresFieldNamesTheTrailAccepts()
    {
        foreach (var (tool, marker) in SensitiveProfileReads.CatalogOf(McpServiceRegistration.McpAssembly))
        {
            var build = () => new AuditEntry(Person, Person, AuditActorKind.McpPersonal, AuditAction.Read, tool, null, marker.FieldNames);
            build.Should().NotThrow($"{tool} must declare identifiers, or every call to it would be refused");
        }
    }

    [Fact]
    public void ServiceTokenPrincipals_AreToldApartFromPersonalOnes()
    {
        var service = new ClaimsPrincipal(new ClaimsIdentity([new Claim("scope", "mcp.service")], "test"));
        var personal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(Permissions.ClaimType, Permissions.McpConnect)], "test"));

        McpTokens.IsServiceToken(service).Should().BeTrue();
        McpTokens.IsServiceToken(personal).Should().BeFalse();
        McpTokens.IsServiceToken(null).Should().BeFalse("stdio has no HTTP principal and is the person's own session");
    }

    [McpServerToolType]
    public sealed class FakeProfileTools
    {
        [McpServerTool(Name = MarkedTool)]
        [SensitiveProfile("Cadence", "Amount")]
        [Description("A tool that returns declared profile data.")]
        public Task<string> GetProfile() => Task.FromResult("never audited");

        [McpServerTool(Name = UnmarkedTool)]
        [Description("A tool that does not.")]
        public Task<string> GetPrice() => Task.FromResult("n/a");
    }

    [McpServerToolType]
    [SensitiveProfile("Label")]
    public sealed class FakeClassMarkedTools
    {
        [McpServerTool(Name = "fake_class_marked")]
        [Description("Marked through its class.")]
        public Task<string> Get() => Task.FromResult("n/a");
    }

    private sealed class FixedUser(Guid? userId) : ICurrentUser
    {
        public Guid? UserId { get; } = userId;
    }

    private sealed class RecordingTrail : IAuditTrail
    {
        public List<AuditEntry> Entries { get; } = [];
        public Exception? Failure { get; init; }
        public Action? OnRecord { get; set; }

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
