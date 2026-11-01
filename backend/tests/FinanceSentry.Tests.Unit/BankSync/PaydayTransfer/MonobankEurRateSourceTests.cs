namespace FinanceSentry.Tests.Unit.BankSync.PaydayTransfer;

using System.Net;
using System.Text;
using FinanceSentry.Modules.BankSync.Infrastructure.Monobank;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

public class MonobankEurRateSourceTests
{
    private const string Body = """
        [
          {"currencyCodeA":840,"currencyCodeB":980,"date":1793000000,"rateBuy":41.00,"rateSell":41.50},
          {"currencyCodeA":978,"currencyCodeB":980,"date":1793000100,"rateBuy":50.00,"rateSell":51.00},
          {"currencyCodeA":978,"currencyCodeB":840,"date":1793000200,"rateBuy":1.1,"rateSell":1.2}
        ]
        """;

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero));
    private readonly StubHandler _handler = new();

    private MonobankEurRateSource Source()
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(MonobankEurRateSource.HttpClientName))
            .Returns(() => new HttpClient(_handler, disposeHandler: false) { BaseAddress = new Uri("https://example.test") });
        return new MonobankEurRateSource(factory.Object, _clock, NullLogger<MonobankEurRateSource>.Instance);
    }

    [Fact]
    public async Task Reads_the_eur_to_uah_rate_buy_and_its_publication_time()
    {
        _handler.Respond(HttpStatusCode.OK, Body);

        var rate = await Source().GetAsync();

        rate.Should().NotBeNull();
        rate!.RateBuy.Should().Be(50.00m);
        rate.PublishedAt.Should().Be(DateTimeOffset.FromUnixTimeSeconds(1793000100));
        _handler.LastPath.Should().Be("/bank/currency");
    }

    [Fact]
    public async Task Caches_the_answer_within_the_ttl_and_refetches_after_it()
    {
        _handler.Respond(HttpStatusCode.OK, Body);
        var source = Source();

        await source.GetAsync();
        await source.GetAsync();
        _handler.Calls.Should().Be(1);

        _clock.Advance(MonobankEurRateSource.CacheTtl + TimeSpan.FromSeconds(1));
        await source.GetAsync();
        _handler.Calls.Should().Be(2);
    }

    [Fact]
    public async Task Keeps_serving_the_last_rate_when_a_refresh_fails()
    {
        _handler.Respond(HttpStatusCode.OK, Body);
        var source = Source();
        await source.GetAsync();

        _clock.Advance(MonobankEurRateSource.CacheTtl + TimeSpan.FromSeconds(1));
        _handler.Respond(HttpStatusCode.TooManyRequests, "{}");
        var rate = await source.GetAsync();

        rate!.RateBuy.Should().Be(50.00m);
    }

    [Fact]
    public async Task Returns_null_when_the_first_fetch_fails()
    {
        _handler.Respond(HttpStatusCode.InternalServerError, "{}");

        (await Source().GetAsync()).Should().BeNull();
    }

    [Fact]
    public async Task Does_not_hit_the_endpoint_again_within_the_ttl_after_a_failed_first_fetch()
    {
        _handler.Respond(HttpStatusCode.TooManyRequests, "{}");
        var source = Source();

        (await source.GetAsync()).Should().BeNull();
        (await source.GetAsync()).Should().BeNull();
        _handler.Calls.Should().Be(1, "the endpoint allows one call a minute, so a failure backs off for the TTL");

        _clock.Advance(MonobankEurRateSource.CacheTtl + TimeSpan.FromSeconds(1));
        _handler.Respond(HttpStatusCode.OK, Body);
        (await source.GetAsync())!.RateBuy.Should().Be(50.00m);
        _handler.Calls.Should().Be(2);
    }

    [Fact]
    public async Task Returns_null_when_the_pair_or_its_buy_rate_is_missing()
    {
        _handler.Respond(HttpStatusCode.OK, """[{"currencyCodeA":978,"currencyCodeB":980,"date":1793000100,"rateCross":50.5}]""");

        (await Source().GetAsync()).Should().BeNull();
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private HttpStatusCode _status = HttpStatusCode.OK;
        private string _body = "[]";

        public int Calls { get; private set; }

        public string? LastPath { get; private set; }

        public void Respond(HttpStatusCode status, string body) => (_status, _body) = (status, body);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            LastPath = request.RequestUri!.AbsolutePath;
            return Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json"),
            });
        }
    }
}
