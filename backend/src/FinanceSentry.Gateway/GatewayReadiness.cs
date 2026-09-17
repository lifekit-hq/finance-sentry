namespace FinanceSentry.Gateway;

using Yarp.ReverseProxy.Model;

/// <summary>
/// Readiness of the edge gateway (platform contract, spec 048): the process is <em>ready</em> only
/// when every YARP cluster has at least one destination that its active (api, frontend) or passive (mcp)
/// health checks have not marked unhealthy. <see cref="ClusterDestinationsState.AvailableDestinations"/>
/// is not used: YARP's default HealthyOrPanic policy lists every destination there when none is healthy.
/// A destination whose health is still unknown counts as available, so a cold gateway is ready until a
/// probe says otherwise. Pure so the evaluation is unit-testable without booting the proxy.
/// </summary>
public static class GatewayReadiness
{
    public const string ReadyStatus = "ready";
    public const string NotReadyStatus = "not_ready";

    public static GatewayReadinessReport Evaluate(IEnumerable<ClusterState> clusters)
    {
        var report = clusters
            .Select(cluster => new ClusterReadiness(
                cluster.ClusterId,
                cluster.DestinationsState.AllDestinations.Count(IsHealthy),
                cluster.DestinationsState.AllDestinations.Count))
            .OrderBy(cluster => cluster.Id, StringComparer.Ordinal)
            .ToList();

        var ready = report.Count > 0 && report.All(cluster => cluster.Available > 0);
        return new GatewayReadinessReport(ready ? ReadyStatus : NotReadyStatus, ready, report);
    }

    private static bool IsHealthy(DestinationState destination) =>
        destination.Health.Active != DestinationHealth.Unhealthy
        && destination.Health.Passive != DestinationHealth.Unhealthy;
}

/// <summary>The JSON body of <c>GET /gateway/ready</c>: <c>{ status, clusters: [{ id, available, total }] }</c>.</summary>
public sealed record GatewayReadinessReport(string Status, bool Ready, IReadOnlyList<ClusterReadiness> Clusters);

public sealed record ClusterReadiness(string Id, int Available, int Total);
