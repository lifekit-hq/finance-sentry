namespace FinanceSentry.Modules.BrokerageSync.Application.Connect;

public interface IIbkrFlexConnector
{
    /// <summary>Sets or replaces the caller's Flex credential (upsert — a replace never conflicts).</summary>
    Task ConnectAsync(Guid userId, ConnectIbkrFlexArtifacts artifacts, CancellationToken ct);

    /// <summary>
    /// Runs the query with the given, not-yet-saved pair and summarises what came back. Persists
    /// nothing; an IBKR rejection surfaces as an <c>IbkrFlexException</c> with an actionable code.
    /// </summary>
    Task<IbkrFlexPreview> PreviewAsync(Guid userId, ConnectIbkrFlexArtifacts artifacts, CancellationToken ct);
}
