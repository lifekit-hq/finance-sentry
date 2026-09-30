namespace FinanceSentry.API.Logging;

using Serilog;

/// <summary>
/// Last-chance logging for an unhandled managed exception that is about to kill the process. The
/// runtime gives no further chance after <see cref="AppDomain.UnhandledException"/>, so the exception is
/// logged at Fatal and the Serilog pipeline is flushed here — otherwise buffered sink output (the rolling
/// file, Loki batches) is lost with the process and the crash leaves no trace.
/// </summary>
public static class FatalErrorHook
{
    /// <summary>Subscribes <see cref="OnUnhandledException"/> to the current app domain.</summary>
    public static void Register() => AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

    public static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        Log.Fatal(e.ExceptionObject as Exception, "Unhandled exception; process terminating");
        Log.CloseAndFlush();
    }
}
