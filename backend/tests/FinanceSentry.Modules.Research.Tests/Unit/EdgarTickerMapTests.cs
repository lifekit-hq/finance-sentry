namespace FinanceSentry.Modules.Research.Tests.Unit;

using System.Net;
using System.Text;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.Application.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

/// <summary>
/// #1375: the ticker→CIK map refreshes on a TTL, retries a failed refresh, serves the last good copy while the
/// refresh keeps failing (up to a ceiling), logs every failed refresh at error level and reports its health.
/// </summary>
public sealed class EdgarTickerMapTests
{
    private const string MapJson = """{"0":{"cik_str":320193,"ticker":"AAPL","title":"Apple Inc."},"1":{"cik_str":723125,"ticker":"MU","title":"Micron"}}""";

    private static readonly DateTimeOffset Start = new(2026, 11, 11, 8, 0, 0, TimeSpan.Zero);

    private static readonly EdgarTickerMapOptions NoBackoff = new() { RetryDelays = [] };

    [Fact]
    public void BeforeAnyRead_HealthIsNeverChecked()
    {
        var (map, _, _) = Create(_ => Json(MapJson));

        map.Read().State.Should().Be(DataSourceHealthState.NeverChecked);
        map.Read().LastSuccessAt.Should().BeNull();
    }

    [Fact]
    public async Task FailedFetch_IsRetriedWithinTheSameRefresh_AndRecovers()
    {
        var calls = 0;
        var (map, _, _) = Create(_ => ++calls < 3 ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : Json(MapJson));

        var read = await map.GetAsync("sec-edgar", CancellationToken.None);

        read.Map.Should().ContainKey("MU").WhoseValue.Should().Be("0000723125");
        read.Stale.Should().BeFalse();
        calls.Should().Be(3);
        map.Read().State.Should().Be(DataSourceHealthState.Healthy);
        map.Read().ConsecutiveFailures.Should().Be(0);
    }

    [Fact]
    public async Task EveryAttemptFailing_WithNoMapLoaded_ReportsUnavailable_AndLogsAnError()
    {
        var calls = 0;
        var (map, _, log) = Create(_ =>
        {
            calls++;
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        });

        var read = await map.GetAsync("sec-edgar", CancellationToken.None);

        read.Map.Should().BeNull();
        calls.Should().Be(3);
        var health = map.Read();
        health.State.Should().Be(DataSourceHealthState.Unavailable);
        health.ConsecutiveFailures.Should().Be(1);
        health.LastFailureAt.Should().Be(Start);
        health.Detail.Should().Contain("cannot resolve any ticker");
        log.Entries.Should().Contain(e => e.Level == LogLevel.Error && e.Message.Contains("no map is loaded"));
    }

    [Fact]
    public async Task AfterAFailedRefresh_CallersAreAnsweredFromWhatIsHeld_UntilTheCooldownPasses()
    {
        var calls = 0;
        var (map, clock, _) = Create(_ =>
        {
            calls++;
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        });

        await map.GetAsync("sec-edgar", CancellationToken.None);
        await map.GetAsync("sec-edgar", CancellationToken.None);
        calls.Should().Be(3, "the second caller is inside the failure cooldown and runs no retry round of its own");

        clock.Advance(NoBackoff.FailureCooldown + TimeSpan.FromSeconds(1));
        await map.GetAsync("sec-edgar", CancellationToken.None);
        calls.Should().Be(6);
        map.Read().ConsecutiveFailures.Should().Be(2);
    }

    [Fact]
    public async Task LoadedMap_IsServedWithoutAFetch_UntilTheTtlExpires_ThenRefreshed()
    {
        var calls = 0;
        var (map, clock, _) = Create(_ =>
        {
            calls++;
            return Json(MapJson);
        });

        await map.GetAsync("sec-edgar", CancellationToken.None);
        clock.Advance(TimeSpan.FromHours(23));
        await map.GetAsync("sec-edgar", CancellationToken.None);
        calls.Should().Be(1);

        clock.Advance(TimeSpan.FromHours(2));
        await map.GetAsync("sec-edgar", CancellationToken.None);
        calls.Should().Be(2);
        map.Read().LastSuccessAt.Should().Be(clock.GetUtcNow());
    }

    [Fact]
    public async Task RefreshFailingAfterTheTtl_ServesTheStaleCopy_AndReportsStale_ThenRecovers()
    {
        var healthy = true;
        var (map, clock, log) = Create(_ => healthy ? Json(MapJson) : new HttpResponseMessage(HttpStatusCode.BadGateway));
        await map.GetAsync("sec-edgar", CancellationToken.None);

        clock.Advance(TimeSpan.FromHours(25));
        healthy = false;
        var stale = await map.GetAsync("sec-edgar", CancellationToken.None);

        stale.Map.Should().ContainKey("AAPL");
        stale.Stale.Should().BeTrue();
        map.Read().State.Should().Be(DataSourceHealthState.Stale);
        map.Read().Detail.Should().Contain("serving the copy");
        log.Entries.Should().Contain(e => e.Level == LogLevel.Error && e.Message.Contains("serving the copy loaded at"));

        clock.Advance(NoBackoff.FailureCooldown + TimeSpan.FromSeconds(1));
        healthy = true;
        var recovered = await map.GetAsync("sec-edgar", CancellationToken.None);

        recovered.Stale.Should().BeFalse();
        map.Read().State.Should().Be(DataSourceHealthState.Healthy);
        map.Read().ConsecutiveFailures.Should().Be(0);
        log.Entries.Should().Contain(e => e.Level == LogLevel.Information && e.Message.Contains("recovered"));
    }

    [Fact]
    public async Task StaleCopyPastTheCeiling_IsNoLongerServed()
    {
        var healthy = true;
        var (map, clock, _) = Create(_ => healthy ? Json(MapJson) : new HttpResponseMessage(HttpStatusCode.BadGateway));
        await map.GetAsync("sec-edgar", CancellationToken.None);

        clock.Advance(TimeSpan.FromDays(8));
        healthy = false;
        var read = await map.GetAsync("sec-edgar", CancellationToken.None);

        read.Map.Should().BeNull();
        map.Read().State.Should().Be(DataSourceHealthState.Unavailable);
    }

    [Fact]
    public async Task PayloadWithNoUsableRows_IsAFailure_NotAnEmptyMap()
    {
        var (map, _, _) = Create(_ => Json("{}"));

        var read = await map.GetAsync("sec-edgar", CancellationToken.None);

        read.Map.Should().BeNull();
        map.Read().State.Should().Be(DataSourceHealthState.Unavailable);
    }

    [Fact]
    public async Task CallerCancellation_Propagates_InsteadOfBeingCountedAsAProviderFailure()
    {
        var (map, _, _) = Create(_ => Json(MapJson));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => map.GetAsync("sec-edgar", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        map.Read().ConsecutiveFailures.Should().Be(0);
    }

    [Fact]
    public async Task ConcurrentCallers_ShareOneRefresh()
    {
        var calls = 0;
        var (map, _, _) = Create(_ =>
        {
            Interlocked.Increment(ref calls);
            return Json(MapJson);
        });

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => map.GetAsync("sec-edgar", CancellationToken.None)));

        calls.Should().Be(1);
    }

    private static (EdgarTickerMap Map, ManualTimeProvider Clock, ListLogger Log) Create(
        Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("sec-edgar"))
            .Returns(() => new HttpClient(new Stub(respond)));
        var clock = new ManualTimeProvider(Start);
        var log = new ListLogger();
        return (new EdgarTickerMap(factory.Object, clock, log, NoBackoff), clock, log);
    }

    private static HttpResponseMessage Json(string body)
        => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class Stub(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.Run(() => respond(request), cancellationToken);
    }

    internal sealed class ListLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (Entries)
            {
                Entries.Add((logLevel, formatter(state, exception)));
            }
        }
    }
}
