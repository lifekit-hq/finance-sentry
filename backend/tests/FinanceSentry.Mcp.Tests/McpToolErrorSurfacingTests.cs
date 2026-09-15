using FinanceSentry.Mcp.Middleware;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Xunit;

namespace FinanceSentry.Mcp.Tests;

/// <summary>
/// Issue #626: every <c>save_thesis</c> call came back as <c>"An error occurred invoking
/// 'save_thesis'."</c> — the SDK's blanket string, which names no cause and left the failure
/// undiagnosable from the caller's side. These tests pin the filter that replaces it: the real
/// exception reaches the caller as an <see cref="McpException"/> (the one type the SDK propagates
/// a message from) and the full exception reaches the log.
/// </summary>
public sealed class McpToolErrorSurfacingTests
{
    private const string ToolName = "save_thesis";

    private static McpRequestFilter<CallToolRequestParams, CallToolResult> RegisteredFilter()
    {
        var services = new ServiceCollection();
        services.AddMcpServer().WithToolErrorSurfacing();

        return FiltersOf(services).Should().ContainSingle(
            "the host installs exactly one tool-error filter").Subject;
    }

    private static IList<McpRequestFilter<CallToolRequestParams, CallToolResult>> FiltersOf(
        IServiceCollection services)
        => services.BuildServiceProvider()
            .GetRequiredService<IOptions<McpServerOptions>>()
            .Value.Filters.Request.CallToolFilters;

    [Fact]
    public void RegisteringTheTools_InstallsTheFilter_InTheSameCall()
    {
        var services = new ServiceCollection();
        services.AddMcpServer().WithFinanceSentryTools(McpServiceRegistration.McpAssembly);

        FiltersOf(services).Should().ContainSingle(
            "both transports register tools through WithFinanceSentryTools precisely so a "
            + "transport cannot serve tools whose failures are still the SDK's blanket string");

        services.BuildServiceProvider()
            .GetRequiredService<IOptions<McpServerOptions>>()
            .Value.ToolCollection.Should().NotBeNullOrEmpty("the tools must still be registered");
    }

    private static async Task<Exception> InvokeThrowing(
        Exception thrown, IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var pipeline = RegisteredFilter()(
            (_, _) => throw thrown);

        var context = TestRequestContext.ForToolCall(ToolName, services);

        return await Record.ExceptionAsync(() => pipeline(context, cancellationToken).AsTask())
            ?? throw new InvalidOperationException("the filter swallowed the failure");
    }

    [Fact]
    public async Task AToolFailure_ReachesTheCaller_AsAMessageThatNamesTheCause()
    {
        await using var services = new ServiceCollection().BuildServiceProvider();

        var surfaced = await InvokeThrowing(
            new InvalidOperationException("column \"EntryPrice\" does not exist"), services);

        surfaced.Should().BeOfType<McpException>(
            "only an McpException has its message propagated to the caller by the SDK");
        surfaced.Message.Should().Contain(ToolName)
            .And.Contain(nameof(InvalidOperationException))
            .And.Contain("column \"EntryPrice\" does not exist");
    }

    [Fact]
    public async Task AWrappedFailure_AlsoCarriesTheInnermostMessage()
    {
        await using var services = new ServiceCollection().BuildServiceProvider();

        var surfaced = await InvokeThrowing(
            new InvalidOperationException(
                "An error occurred while saving the entity changes.",
                new TimeoutException("42P01: relation \"research.thesis_events\" does not exist")),
            services);

        surfaced.Message.Should()
            .Contain("An error occurred while saving the entity changes.")
            .And.Contain("relation \"research.thesis_events\" does not exist",
                "the detail that identifies the fault lives on the innermost exception");
    }

    [Fact]
    public async Task AToolFailure_IsLoggedWithItsFullException()
    {
        var recorder = new RecordingLoggerProvider();
        await using var services = new ServiceCollection()
            .AddLogging(b => b.SetMinimumLevel(LogLevel.Trace).AddProvider(recorder))
            .BuildServiceProvider();

        var thrown = new InvalidOperationException("thesis_events is unavailable");
        await InvokeThrowing(thrown, services);

        var logged = recorder.Entries.Should().ContainSingle().Subject;
        logged.Level.Should().Be(LogLevel.Error);
        logged.Exception.Should().BeSameAs(thrown, "the stack trace is the half the caller never sees");
        logged.Message.Should().Contain(ToolName);
    }

    [Fact]
    public async Task ACallerCancellation_PassesThrough_Unwrapped()
    {
        await using var services = new ServiceCollection().BuildServiceProvider();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var surfaced = await InvokeThrowing(
            new OperationCanceledException(), services, cancelled.Token);

        surfaced.Should().BeOfType<OperationCanceledException>(
            "a cancelled call is not a tool fault and must not be reported as one");
    }

    [Fact]
    public async Task AnUpstreamTimeout_IsSurfaced_NotMistakenForACancellation()
    {
        await using var services = new ServiceCollection().BuildServiceProvider();

        // HttpClient signals its own timeout as a TaskCanceledException on the request's token,
        // with our token untouched — a real fault wearing a cancellation's clothes.
        var surfaced = await InvokeThrowing(
            new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout"),
            services,
            CancellationToken.None);

        surfaced.Should().BeOfType<McpException>();
        surfaced.Message.Should().Contain("HttpClient.Timeout",
            "an upstream that timed out is the single most useful thing to tell the caller");
    }

    [Fact]
    public async Task AnMcpException_IsLeftAlone()
    {
        await using var services = new ServiceCollection().BuildServiceProvider();
        var thrown = new McpException("entryPrice must be positive");

        var surfaced = await InvokeThrowing(thrown, services);

        surfaced.Should().BeSameAs(thrown,
            "a tool that already speaks MCP has said what it meant — re-wrapping would double the prefix");
    }

    private sealed class RecordingLoggerProvider : ILoggerProvider
    {
        public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];

        public ILogger CreateLogger(string categoryName) => new RecordingLogger(this.Entries);

        public void Dispose()
        {
        }

        private sealed class RecordingLogger(List<(LogLevel, string, Exception?)> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
                => entries.Add((logLevel, formatter(state, exception), exception));
        }
    }
}
