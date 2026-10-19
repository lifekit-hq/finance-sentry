namespace FinanceSentry.Tests.Integration.Radar;

using System.Net;
using System.Net.Http.Json;
using FinanceSentry.Core.Headlines;
using FinanceSentry.Modules.Radar.Application.Services;
using FinanceSentry.Modules.Radar.Domain;
using FinanceSentry.Tests.Integration.Shared;
using FinanceSentry.Tests.Integration.Wealth;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

// ── Contract tests: GET /api/v1/radar/book-performance ───────────────────────

public class BookPerformanceContractTests(WealthApiFactory factory) : IClassFixture<WealthApiFactory>
{
    private readonly Mock<IBookPerformanceService> _performance = new(MockBehavior.Loose);

    [Fact]
    public async Task GetBookPerformance_NoAuth_Returns401()
    {
        var response = await factory.CreateClient().GetAsync("/api/v1/radar/book-performance");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetBookPerformance_ReturnsEveryWindow_ForTheSignedInPerson()
    {
        var asOf = new DateOnly(2026, 10, 19);
        _performance
            .Setup(p => p.GetAsync(factory.TestUserId, It.IsAny<IReadOnlyList<BookPerformancePeriod>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BookPerformanceResult(
            [
                new PeriodTwr(
                    BookPerformancePeriod.OneYear, asOf.AddYears(-1), 0.12m, 0.1m, 0.02m, "outperform",
                    BookTwrFrom: asOf.AddYears(-1), BookTwrTo: asOf,
                    BookTwrReasons: [new HeadlineReason(HeadlineReasonCode.UnknownFlow, "ibkr")],
                    BookTwrQuality: HeadlineQuality.Estimated),
            ], asOf));

        var response = await Client().GetAsync("/api/v1/radar/book-performance");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<BookPerformanceShape>();
        body!.AsOf.Should().Be("2026-10-19");
        var period = body.Periods.Should().ContainSingle().Subject;
        period.Period.Should().Be((int)BookPerformancePeriod.OneYear, "the enum has no string converter on the API");
        period.BookTwr.Should().Be(0.12m);
        period.SpyTwr.Should().Be(0.1m);
        period.BookTwrFrom.Should().Be("2025-10-19");
        period.BookTwrQuality.Should().Be("ESTIMATED");
        period.BookTwrReasons.Should().ContainSingle(r => r.Code == "UNKNOWN_FLOW" && r.Source == "ibkr");
        _performance.Verify(p => p.GetAsync(
            factory.TestUserId,
            It.Is<IReadOnlyList<BookPerformancePeriod>>(ps => ps.Count == 4),
            It.IsAny<CancellationToken>()));
    }

    // The mock replaces the service on a derived host, which has its own in-memory users: seed the person there
    // and reuse the signed cookie the shared factory issues for them.
    private HttpClient Client()
    {
        var withMock = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddScoped(_ => _performance.Object)));
        TestUsers.EnsureExists(withMock.Services, factory.TestUserId);
        var client = withMock.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("Cookie", factory.CreateAuthenticatedClient().DefaultRequestHeaders.GetValues("Cookie"));
        return client;
    }
}

public record BookPerformanceReasonShape(string Code, string? Source);
public record BookPerformancePeriodShape(
    int Period, decimal? BookTwr, decimal? SpyTwr, string? BookTwrFrom, string? BookTwrTo,
    List<BookPerformanceReasonShape>? BookTwrReasons, string? BookTwrQuality);
public record BookPerformanceShape(List<BookPerformancePeriodShape> Periods, string AsOf);
