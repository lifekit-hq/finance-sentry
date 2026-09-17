namespace FinanceSentry.Gateway;

using Yarp.ReverseProxy.Model;

/// <summary>
/// Readiness of the edge gateway (platform contract, spec 048): the process is <em>ready</em> only
/// when every YARP cluster has at least one destination it would route to. YARP already tracks that
/// through its active (api, frontend) and passive (mcp) health checks and exposes it as
/// <see cref="ClusterDestinationsState.AvailableDestinations"/>; a destination whose health is still
/// unknown counts as available, so a cold gateway is ready until a probe says otherwise. Pure so the
/// evaluation is unit-testable without booting the proxy.
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
                cluster.DestinationsState.AvailableDestinations.Count,
                cluster.DestinationsState.AllDestinations.Count))
            .OrderBy(cluster => cluster.Id, StringComparer.Ordinal)
            .ToList();

        var ready = report.Count > 0 && report.All(cluster => cluster.Available > 0);
        return new GatewayReadinessReport(ready ? ReadyStatus : NotReadyStatus, ready, report);
    }
}

/// <summary>The JSON body of <c>GET /gateway/ready</c>: <c>{ status, clusters: [{ id, available, total }] }</c>.</summary>
public sealed record GatewayReadinessReport(string Status, bool Ready, IReadOnlyList<ClusterReadiness> Clusters);

public sealed record ClusterReadiness(string Id, int Available, int Total);
