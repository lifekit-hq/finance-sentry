namespace FinanceSentry.Tests.Unit.Logging;

using FinanceSentry.API.Logging;
using FluentAssertions;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Xunit;

/// <summary>
/// The unhandled-exception hook must write the crash at Fatal and flush (dispose) the logger before
/// the process dies. Swaps the static <see cref="Log.Logger"/>; safe because parallelization is
/// disabled for this assembly.
/// </summary>
public class FatalErrorHookTests
{
    [Fact]
    public void OnUnhandledException_LogsFatalWithExceptionAndFlushes()
    {
        var sink = new CapturingSink();
        var previous = Log.Logger;
        Log.Logger = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();
        try
        {
            var boom = new InvalidOperationException("boom");

            FatalErrorHook.OnUnhandledException(this, new UnhandledExceptionEventArgs(boom, isTerminating: true));

            var logged = sink.Events.Should().ContainSingle().Subject;
            logged.Level.Should().Be(LogEventLevel.Fatal);
            logged.Exception.Should().BeSameAs(boom);
            logged.RenderMessage().Should().Contain("process terminating");
            sink.Disposed.Should().BeTrue("Log.CloseAndFlush must dispose the logger so buffered sinks flush");
        }
        finally
        {
            Log.Logger = previous;
        }
    }

    private sealed class CapturingSink : ILogEventSink, IDisposable
    {
        public List<LogEvent> Events { get; } = [];

        public bool Disposed { get; private set; }

        public void Emit(LogEvent logEvent) => Events.Add(logEvent);

        public void Dispose() => Disposed = true;
    }
}
