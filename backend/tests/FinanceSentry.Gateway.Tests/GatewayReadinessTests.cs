namespace FinanceSentry.Gateway.Tests;

using FinanceSentry.Gateway;
using Xunit;
using Yarp.ReverseProxy.Model;

/// <summary>
/// <see cref="GatewayReadiness"/> (spec 048, US2): ready only when every cluster has a destination YARP
/// would route to; the report names each cluster with its available/total counts either way.
/// </summary>
public sealed class GatewayReadinessTests
{
    [Fact]
    public void EveryClusterHasAnAvailableDestination_IsReady()
    {
        var report = GatewayReadiness.Evaluate([
            Cluster("api", all: 1, available: 1),
            Cluster("frontend", all: 2, available: 1),
        ]);

        Assert.True(report.Ready);
        Assert.Equal(GatewayReadiness.ReadyStatus, report.Status);
        Assert.Collection(report.Clusters,
            c => Assert.Equal(("api", 1, 1), (c.Id, c.Available, c.Total)),
            c => Assert.Equal(("frontend", 1, 2), (c.Id, c.Available, c.Total)));
    }

    [Fact]
    public void OneClusterWithNoAvailableDestination_IsNotReady_AndNamesIt()
    {
        var report = GatewayReadiness.Evaluate([
            Cluster("api", all: 1, available: 0),
            Cluster("frontend", all: 1, available: 1),
        ]);

        Assert.False(report.Ready);
        Assert.Equal(GatewayReadiness.NotReadyStatus, report.Status);
        var api = Assert.Single(report.Clusters, c => c.Id == "api");
        Assert.Equal(0, api.Available);
        Assert.Equal(1, api.Total);
    }

    [Fact]
    public void NoClusters_IsNotReady()
    {
        var report = GatewayReadiness.Evaluate([]);

        Assert.False(report.Ready);
        Assert.Empty(report.Clusters);
    }

    private static ClusterState Cluster(string id, int all, int available)
    {
        var destinations = Enumerable.Range(0, all)
            .Select(i => new DestinationState($"{id}-{i}"))
            .ToList();
        return new ClusterState(id)
        {
            DestinationsState = new ClusterDestinationsState(destinations, destinations.Take(available).ToList()),
        };
    }
}
