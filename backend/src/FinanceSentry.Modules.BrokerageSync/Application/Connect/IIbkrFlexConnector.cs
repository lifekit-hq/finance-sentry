namespace FinanceSentry.Modules.BrokerageSync.Application.Connect;

public interface IIbkrFlexConnector
{
    /// <summary>Sets or replaces the caller's Flex credential (upsert — a replace never conflicts).</summary>
    Task ConnectAsync(Guid userId, ConnectIbkrFlexArtifacts artifacts, CancellationToken ct);
}
