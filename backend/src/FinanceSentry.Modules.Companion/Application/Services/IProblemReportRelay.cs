namespace FinanceSentry.Modules.Companion.Application.Services;

public enum RelayStatus
{
    /// <summary>No relay key is set up; the report stays pending and nothing was attempted.</summary>
    NotConfigured,

    /// <summary>The relay saved the note (or recognised the request id as one it already has).</summary>
    Sent,

    /// <summary>The relay is rate limiting this sender; try again later, without counting it against the report.</summary>
    Deferred,

    Failed,
}

public readonly record struct RelayResult(RelayStatus Status, string? Detail = null);

/// <summary>Hands one note to the fleet inbox. The same <c>requestId</c> always yields the same note, so a retry is safe.</summary>
public interface IProblemReportRelay
{
    bool IsConfigured { get; }

    Task<RelayResult> SendAsync(string requestId, string body, CancellationToken ct = default);
}
