namespace FinanceSentry.Modules.Companion.Infrastructure.Services;

using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using FinanceSentry.Modules.Companion.Application.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Sends a note through the host's <c>kit-relay</c> forced command: <c>ssh -F &lt;config&gt; relay note &lt;request-id&gt;</c> with the
/// body on stdin. The ssh config (host, user, identity, pinned host key) is the one the relay key ships with, so nothing
/// else is configured here; with no such file the relay is not configured and nothing is attempted. Arguments go to ssh
/// as a list, never through a shell, and the request id is the report's own <c>fs-report-&lt;id&gt;</c>.
/// </summary>
public sealed class SshProblemReportRelay(
    IOptions<ProblemReportOptions> options,
    ILogger<SshProblemReportRelay> logger) : IProblemReportRelay
{
    // kit-relay's exit code for "rate limit"; 64 and 65 are refusals of the request or body.
    private const int RateLimitedExit = 75;

    private const int MaxDetailLength = 200;

    private readonly ProblemReportOptions _options = options.Value;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_options.RelayConfigPath) && File.Exists(_options.RelayConfigPath);

    public async Task<RelayResult> SendAsync(string requestId, string body, CancellationToken ct = default)
    {
        if (!IsConfigured)
            return new RelayResult(RelayStatus.NotConfigured);

        var psi = new ProcessStartInfo(_options.SshPath)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardInputEncoding = new UTF8Encoding(false),
        };
        foreach (var arg in new[]
                 {
                     "-F", _options.RelayConfigPath!,
                     "-o", "BatchMode=yes",
                     "-o", "ConnectTimeout=10",
                     _options.RelayHost, "note", requestId,
                 })
        {
            psi.ArgumentList.Add(arg);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.ForwardTimeoutSeconds));
        try
        {
            using var process = new Process { StartInfo = psi };
            process.Start();
            var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
            try
            {
                try
                {
                    await process.StandardInput.WriteAsync(body.AsMemory(), timeout.Token);
                    process.StandardInput.Close();
                }
                catch (IOException)
                {
                    // ssh exited before reading the body (refused at connect); its exit code and stderr say why.
                }

                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                throw;
            }

            await stdout;
            var detail = Shorten(await stderr);
            return process.ExitCode switch
            {
                0 => new RelayResult(RelayStatus.Sent),
                RateLimitedExit => new RelayResult(RelayStatus.Deferred, detail),
                var code => new RelayResult(RelayStatus.Failed, $"relay exit {code}: {detail}".TrimEnd(' ', ':')),
            };
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("Problem report relay timed out for {RequestId}", requestId);
            return new RelayResult(RelayStatus.Failed, "relay timed out");
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            logger.LogWarning(ex, "Problem report relay could not run ssh for {RequestId}", requestId);
            return new RelayResult(RelayStatus.Failed, "ssh could not be started");
        }
    }

    private static string Shorten(string text)
    {
        var line = text.Trim().ReplaceLineEndings(" ");
        return line.Length <= MaxDetailLength ? line : line[..MaxDetailLength];
    }
}
