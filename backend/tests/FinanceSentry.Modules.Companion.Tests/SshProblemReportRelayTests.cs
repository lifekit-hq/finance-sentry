namespace FinanceSentry.Modules.Companion.Tests;

using System.Runtime.Versioning;
using FinanceSentry.Modules.Companion.Application.Services;
using FinanceSentry.Modules.Companion.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

/// <summary>
/// The ssh sender against a stub <c>ssh</c> script: not configured without the relay's ssh_config, the argument list
/// and stdin it hands the forced command, and how each exit code reads.
/// </summary>
[UnsupportedOSPlatform("windows")]
public sealed class SshProblemReportRelayTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("fs-relay-test").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string WriteConfig()
    {
        var path = Path.Combine(_dir, "ssh_config");
        File.WriteAllText(path, "Host relay\n");
        return path;
    }

    private string WriteStubSsh(string script)
    {
        var path = Path.Combine(_dir, "ssh");
        File.WriteAllText(path, "#!/bin/sh\n" + script);
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }

    private SshProblemReportRelay Relay(string? configPath, string? sshPath = null, int timeoutSeconds = 20)
        => new(
            Options.Create(new ProblemReportOptions
            {
                RelayConfigPath = configPath,
                SshPath = sshPath ?? "ssh",
                ForwardTimeoutSeconds = timeoutSeconds,
            }),
            NullLogger<SshProblemReportRelay>.Instance);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("/definitely/not/here/ssh_config")]
    public async Task Without_the_relays_ssh_config_it_is_not_configured_and_nothing_runs(string? configPath)
    {
        var marker = Path.Combine(_dir, "ran");
        var relay = Relay(configPath, WriteStubSsh($"touch '{marker}'\n"));

        relay.IsConfigured.Should().BeFalse();
        (await relay.SendAsync("fs-report-1", "body")).Status.Should().Be(RelayStatus.NotConfigured);
        File.Exists(marker).Should().BeFalse();
    }

    [Fact]
    public async Task It_runs_the_note_verb_with_the_request_id_and_writes_the_body_to_stdin()
    {
        var config = WriteConfig();
        var relay = Relay(config, WriteStubSsh($"printf '%s\\n' \"$@\" > '{_dir}/args'\ncat > '{_dir}/stdin'\n"));
        const string body = "report: x\n> hello — ünïcode\n";

        var result = await relay.SendAsync("fs-report-7", body);

        result.Status.Should().Be(RelayStatus.Sent);
        File.ReadAllLines(Path.Combine(_dir, "args")).Should().Equal(
            "-F", config, "-o", "BatchMode=yes", "-o", "ConnectTimeout=10", "relay", "note", "fs-report-7");
        File.ReadAllText(Path.Combine(_dir, "stdin")).Should().Be(body);
        File.ReadAllBytes(Path.Combine(_dir, "stdin")).Take(3).Should().NotEqual([0xEF, 0xBB, 0xBF], "no byte-order mark");
    }

    [Fact]
    public async Task Exit_75_means_the_relay_is_rate_limiting_and_is_deferred()
    {
        var relay = Relay(WriteConfig(), WriteStubSsh("cat >/dev/null\necho 'rate limit' >&2\nexit 75\n"));

        var result = await relay.SendAsync("fs-report-1", "body");

        result.Status.Should().Be(RelayStatus.Deferred);
        result.Detail.Should().Contain("rate limit");
    }

    [Theory]
    [InlineData(64)]
    [InlineData(65)]
    [InlineData(255)]
    public async Task Any_other_nonzero_exit_is_a_failure_with_the_code_and_stderr(int code)
    {
        var relay = Relay(WriteConfig(), WriteStubSsh($"cat >/dev/null\necho 'nope: refused' >&2\nexit {code}\n"));

        var result = await relay.SendAsync("fs-report-1", "body");

        result.Status.Should().Be(RelayStatus.Failed);
        result.Detail.Should().Contain($"exit {code}").And.Contain("nope: refused");
    }

    [Fact]
    public async Task A_stub_that_exits_without_reading_stdin_still_reports_its_exit_code()
    {
        var relay = Relay(WriteConfig(), WriteStubSsh("exit 255\n"));

        var result = await relay.SendAsync("fs-report-1", new string('x', 200_000));

        result.Status.Should().Be(RelayStatus.Failed);
    }

    [Fact]
    public async Task A_missing_ssh_binary_is_a_failure_not_an_exception()
    {
        var relay = Relay(WriteConfig(), Path.Combine(_dir, "no-such-ssh"));

        var result = await relay.SendAsync("fs-report-1", "body");

        result.Status.Should().Be(RelayStatus.Failed);
    }

    [Fact]
    public async Task A_hung_ssh_is_killed_at_the_timeout_and_fails()
    {
        var relay = Relay(WriteConfig(), WriteStubSsh("sleep 30\n"), timeoutSeconds: 1);

        var started = DateTimeOffset.UtcNow;
        var result = await relay.SendAsync("fs-report-1", "body");

        result.Status.Should().Be(RelayStatus.Failed);
        result.Detail.Should().Contain("timed out");
        (DateTimeOffset.UtcNow - started).Should().BeLessThan(TimeSpan.FromSeconds(15));
    }
}
