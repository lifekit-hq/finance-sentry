namespace FinanceSentry.Modules.Companion.Application.Services;

/// <summary>
/// Where problem reports are forwarded, bound from the <c>ProblemReports</c> section. The forwarder is inert until
/// <see cref="RelayConfigPath"/> names an <c>ssh_config</c> file that exists: reports are saved and stay pending.
/// </summary>
public sealed class ProblemReportOptions
{
    public const string SectionName = "ProblemReports";

    /// <summary>
    /// The <c>ssh_config</c> of the relay key (<c>HostName</c>, <c>User</c>, <c>IdentityFile</c> and a pinned
    /// <c>UserKnownHostsFile</c>, as for the other kit-relay senders). Empty, or a file that is not there, = not configured.
    /// </summary>
    public string? RelayConfigPath { get; set; }

    /// <summary>The <c>Host</c> alias in that config the note is sent to.</summary>
    public string RelayHost { get; set; } = "relay";

    public string SshPath { get; set; } = "ssh";

    public int ForwardTimeoutSeconds { get; set; } = 20;
}
