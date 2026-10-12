namespace FinanceSentry.Modules.Companion.Domain;

/// <summary>The numbers behind the problem-report endpoint and its forwarder.</summary>
public static class ProblemReportLimits
{
    public const int TextMaxLength = 1000;

    public const int PerHour = 5;

    public const int PerDay = 20;

    public const int RouteMaxLength = 200;

    public const int VersionMaxLength = 32;

    public const int ClientPartMaxLength = 40;

    public const int MaxForwardAttempts = 6;

    /// <summary>Wait before the next forward attempt after the nth failure (1-based); the last entry repeats.</summary>
    public static readonly IReadOnlyList<TimeSpan> ForwardBackoff =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromHours(1),
        TimeSpan.FromHours(4),
    ];

    /// <summary>The reference shown to the reporter.</summary>
    public static string Reference(long id) => $"FS-R-{id}";

    /// <summary>The relay request id; the relay requires its sender prefix (<c>fs-</c>) and turns a repeat into the same note.</summary>
    public static string RequestId(long id) => $"fs-report-{id}";
}
