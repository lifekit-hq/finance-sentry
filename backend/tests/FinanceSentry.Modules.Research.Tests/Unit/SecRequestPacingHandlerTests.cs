namespace FinanceSentry.Modules.Research.Tests.Unit;

using System.Diagnostics;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Application.Services.Catalyst;
using FluentAssertions;
using Xunit;

/// <summary>
/// The sec-edgar client is paced under the SEC's 10 requests a second: an unpaced thesis-monitor run bursts past
/// it, trips the circuit breaker, and blinds every fundamentals trigger (#1521).
/// </summary>
public sealed class SecRequestPacingHandlerTests
{
    [Fact]
    public async Task Concurrent_requests_are_spaced_by_the_pacer_interval()
    {
        var interval = TimeSpan.FromMilliseconds(100);
        var handler = new SecRequestPacingHandler(new RequestPacer(interval, TimeProvider.System))
        {
            InnerHandler = new OkHandler(),
        };
        using var client = new HttpClient(handler);

        var clock = Stopwatch.StartNew();
        var responses = await Task.WhenAll(
            Enumerable.Range(0, 4).Select(_ => client.GetAsync(new Uri("https://data.sec.gov/x"))));
        clock.Stop();

        responses.Should().OnlyContain(r => r.IsSuccessStatusCode);
        // Four requests need three gaps; the first goes out at once.
        clock.Elapsed.Should().BeGreaterThanOrEqualTo(interval * 3 - TimeSpan.FromMilliseconds(30));
    }

    [Fact]
    public void Default_rate_stays_under_the_SEC_limit_of_ten_a_second()
        => new SecEdgarOptions().RequestsPerSecond.Should().BeInRange(1, 9);

    private sealed class OkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
    }
}
