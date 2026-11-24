namespace FinanceSentry.Tests.Integration.Radar;

using System.Net;
using System.Net.Http.Json;
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

// ── Contract tests: GET /api/v1/radar/risk-picture ───────────────────────────

public class RiskPictureContractTests(WealthApiFactory factory) : IClassFixture<WealthApiFactory>
{
    private readonly Mock<IRiskPictureService> _riskPicture = new(MockBehavior.Loose);
    private readonly Mock<IConcentrationHistoryService> _history = new(MockBehavior.Loose);

    [Fact]
    public async Task GetRiskPicture_NoAuth_Returns401()
    {
        var response = await factory.CreateClient().GetAsync("/api/v1/radar/risk-picture");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetRiskPicture_ReturnsEveryFigureWithItsReason_ForTheSignedInPerson()
    {
        _riskPicture
            .Setup(r => r.GetAsync(factory.TestUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RiskPicture(
                new DateOnly(2026, 1, 30), new DateOnly(2026, 9, 30), 8, 0,
                new RiskFigure(0.182m, null),
                new RiskFigure(0.214m, null),
                new RiskFigure(null, "NotEnoughHistory"),
                DrawdownTolerance: null,
                Top1Symbol: "DRAM", Top1Weight: 0.378m,
                Top3Symbols: ["DRAM", "BTC"], Top3Weight: 0.568m,
                CryptoShare: 0.19m, PositionFlag: 0.3m, Top1OverFlag: true));

        var response = await Client().GetAsync("/api/v1/radar/risk-picture");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<RiskPictureShape>();
        body!.From.Should().Be("2026-01-30");
        body.To.Should().Be("2026-09-30");
        body.MonthsObserved.Should().Be(8);
        body.BetaMonths.Should().Be(0);
        body.Volatility.Value.Should().Be(0.182m);
        body.MaxDrawdown.Value.Should().Be(0.214m);
        body.Beta.Value.Should().BeNull();
        body.Beta.Reason.Should().Be("NotEnoughHistory");
        body.DrawdownTolerance.Should().BeNull();
        body.Top1Symbol.Should().Be("DRAM");
        body.Top1Weight.Should().Be(0.378m);
        body.Top3Symbols.Should().Equal("DRAM", "BTC");
        body.Top3Weight.Should().Be(0.568m);
        body.CryptoShare.Should().Be(0.19m);
        body.PositionFlag.Should().Be(0.3m);
        body.Top1OverFlag.Should().BeTrue();
    }

    [Fact]
    public async Task GetRiskPictureHistory_NoAuth_Returns401()
    {
        var response = await factory.CreateClient().GetAsync("/api/v1/radar/risk-picture/history");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetRiskPictureHistory_ReturnsTheSeriesAndTargetsAsFractions_ForTheSignedInPerson()
    {
        _history
            .Setup(h => h.GetAsync(factory.TestUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConcentrationHistory(
                [
                    new ConcentrationPoint(new DateOnly(2026, 1, 5), 0.8m, 0.1m, null),
                    new ConcentrationPoint(new DateOnly(2026, 6, 5), 0.85m, 0.15m, 0.3m),
                ],
                0.10m, 0.05m, null));

        var response = await Client().GetAsync("/api/v1/radar/risk-picture/history");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ConcentrationHistoryShape>();
        body!.Points.Select(p => (p.Date, p.Top3Share, p.CryptoShare, p.CashShare)).Should().Equal(
            ("2026-01-05", 0.8m, 0.1m, (decimal?)null),
            ("2026-06-05", 0.85m, 0.15m, 0.3m));
        body.CryptoTarget.Should().Be(0.10m);
        body.CashTarget.Should().Be(0.05m);
        body.Reason.Should().BeNull();
    }

    private HttpClient Client()
    {
        var withMock = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddScoped(_ => _riskPicture.Object).AddScoped(_ => _history.Object)));
        TestUsers.EnsureExists(withMock.Services, factory.TestUserId);
        var client = withMock.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("Cookie", factory.CreateAuthenticatedClient().DefaultRequestHeaders.GetValues("Cookie"));
        return client;
    }
}

public record RiskFigureShape(decimal? Value, string? Reason);
public record RiskPictureShape(
    string? From, string? To, int MonthsObserved, int BetaMonths,
    RiskFigureShape Volatility, RiskFigureShape MaxDrawdown, RiskFigureShape Beta,
    decimal? DrawdownTolerance, string? Top1Symbol, decimal? Top1Weight, List<string> Top3Symbols,
    decimal? Top3Weight, decimal? CryptoShare, decimal? PositionFlag, bool? Top1OverFlag);

public record ConcentrationPointShape(string Date, decimal Top3Share, decimal CryptoShare, decimal? CashShare);
public record ConcentrationHistoryShape(
    List<ConcentrationPointShape> Points, decimal? CryptoTarget, decimal? CashTarget, string? Reason);
