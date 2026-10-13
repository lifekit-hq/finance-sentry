namespace FinanceSentry.Tests.Integration.Research;

using System.Net;
using System.Net.Http.Json;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain.Ports;
using FluentAssertions;
using Moq;
using Xunit;

// ── Contract tests: dossier stage 2 ──────────────────────────────────────────
//
// GET /research/assets/{symbol}/chart, PUT/DELETE /research/assets/{symbol}/target and the dossier's
// performance strip, traceable checks and return decomposition. Price history, fills and the IPS cap are
// stubbed through the shared AssetDossierApiFactory; each test uses its own symbol so the shared
// in-memory target store never leaks between tests.

public class AssetDossierChartTargetContractTests(AssetDossierApiFactory factory)
    : IClassFixture<AssetDossierApiFactory>
{
    private static readonly DateOnly FirstDay = new(2025, 10, 13);

    private readonly HttpClient _client = factory.CreateAuthenticatedClient();
    private readonly AssetDossierApiFactory _factory = factory;

    [Fact]
    public async Task GetChart_NoAuth_Returns401()
    {
        var response = await _factory.CreateClient().GetAsync("/api/v1/research/assets/AAPL/chart");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetChart_UnknownRange_Returns400WithErrorCode()
    {
        var response = await _client.GetAsync("/api/v1/research/assets/AAPL/chart?range=2W");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ErrorShape>();
        body!.ErrorCode.Should().Be("INVALID_CHART_RANGE");
    }

    [Fact]
    public async Task GetChart_HeldSymbol_ReturnsRebasedBenchmarkTradesAndAverageCost()
    {
        const string symbol = "CHRT";
        HoldPosition(symbol, quantity: 10m, valueUsd: 1200m, costUsd: 1000m);
        StubHistory(symbol, "6mo", [100m, 110m, 120m]);
        StubHistory(DossierTickers.Benchmark, "6mo", [400m, 420m, 440m]);
        _factory.PositionLedgerMock
            .Setup(s => s.ReadForSymbolAsync(It.IsAny<Guid>(), symbol, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PositionLedger(
                [
                    new LedgerFill("ibkr", FirstDay.AddDays(-30).ToDateTime(TimeOnly.MinValue), 5m, 90m, null, null, "USD"),
                    new LedgerFill("ibkr", FirstDay.AddDays(1).ToDateTime(TimeOnly.MinValue), 5m, 110m, null, null, "USD"),
                ],
                []));

        var response = await _client.GetAsync($"/api/v1/research/assets/{symbol.ToLowerInvariant()}/chart?range=6m");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ChartShape>();
        body!.Symbol.Should().Be(symbol);
        body.Range.Should().Be("6M");
        body.Currency.Should().Be("USD");
        body.Points.Should().HaveCount(3);
        body.Points[0].Benchmark.Should().Be(100m, "the benchmark is rebased onto the asset's first close");
        body.Points[2].Benchmark.Should().Be(110m);
        body.ChangePct.Should().Be(20m);
        body.BenchmarkChangePct.Should().Be(10m);
        body.Trades.Should().ContainSingle("only the fill inside the window is marked")
            .Which.Side.Should().Be("buy");
        body.AverageCost.Should().Be(100m);
        body.BenchmarkLabel.Should().Contain("S&P 500");
    }

    [Fact]
    public async Task GetChart_NoHistory_Returns200WithNoPoints()
    {
        var response = await _client.GetAsync("/api/v1/research/assets/NOHIST/chart");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ChartShape>();
        body!.Range.Should().Be("1Y");
        body.Points.Should().BeEmpty();
        body.ChangePct.Should().BeNull();
    }

    [Fact]
    public async Task PutTarget_ThenDossierShowsIt_AndDeleteClearsIt()
    {
        const string symbol = "TGT";

        var put = await _client.PutAsJsonAsync(
            $"/api/v1/research/assets/{symbol}/target", new { targetPrice = 123.456789m, currency = "usd" });

        put.StatusCode.Should().Be(HttpStatusCode.OK);
        var saved = await put.Content.ReadFromJsonAsync<TargetShape>();
        saved!.TargetPrice.Should().Be(123.4568m);
        saved.Currency.Should().Be("USD");

        var dossier = await _client.GetFromJsonAsync<DossierStage2Shape>($"/api/v1/research/assets/{symbol}/dossier");
        dossier!.UserTarget!.TargetPrice.Should().Be(123.4568m);

        var delete = await _client.DeleteAsync($"/api/v1/research/assets/{symbol}/target");
        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var after = await _client.GetFromJsonAsync<DossierStage2Shape>($"/api/v1/research/assets/{symbol}/dossier");
        after!.UserTarget.Should().BeNull();
    }

    [Fact]
    public async Task DeleteTarget_NoneSet_Returns204()
    {
        var response = await _client.DeleteAsync("/api/v1/research/assets/NOTGT/target");
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(2_000_000_000)]
    public async Task PutTarget_PriceOutOfRange_Returns400WithErrorCode(decimal price)
    {
        var response = await _client.PutAsJsonAsync(
            "/api/v1/research/assets/BAD/target", new { targetPrice = price });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ErrorShape>();
        body!.ErrorCode.Should().Be("INVALID_PRICE_TARGET");
    }

    [Fact]
    public async Task PutTarget_BadCurrency_Returns400WithErrorCode()
    {
        var response = await _client.PutAsJsonAsync(
            "/api/v1/research/assets/BAD/target", new { targetPrice = 10m, currency = "dollars" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ErrorShape>();
        body!.ErrorCode.Should().Be("INVALID_PRICE_TARGET");
    }

    [Fact]
    public async Task PutTarget_IsPerUser()
    {
        const string symbol = "MINE";
        await _client.PutAsJsonAsync($"/api/v1/research/assets/{symbol}/target", new { targetPrice = 50m });

        var other = _factory.CreateAuthenticatedClient(owner: false);
        var dossier = await other.GetFromJsonAsync<DossierStage2Shape>($"/api/v1/research/assets/{symbol}/dossier");

        dossier!.UserTarget.Should().BeNull();
    }

    [Fact]
    public async Task GetDossier_HeldStock_CarriesPerformanceChecksAndReturns()
    {
        const string symbol = "PERF";
        HoldPosition(symbol, quantity: 10m, valueUsd: 1500m, costUsd: 1000m);
        StubHistory(symbol, "1y", [100m, 120m, 150m]);
        StubHistory(DossierTickers.Benchmark, "1y", [400m, 400m, 440m]);
        _factory.PositionCapMock
            .Setup(s => s.GetMaxPositionWeightAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0.10m);

        var body = await _client.GetFromJsonAsync<DossierStage2Shape>($"/api/v1/research/assets/{symbol}/dossier");

        body!.Position!.PositionCapPercent.Should().Be(10m);
        body.Position.Returns!.Coverage.Should().Be("unavailable", "no fills were ingested");
        body.Position.Returns.UnrealizedUsd.Should().Be(500m);
        body.Performance!.LastClose.Should().Be(150m);
        body.Performance.YearChangePct.Should().Be(50m);
        body.Performance.BenchmarkYearChangePct.Should().Be(10m);
        body.Checks!.Axes.Select(a => a.Key).Should().Equal("analysts", "growth", "value", "momentum", "fit");
        var momentum = body.Checks.Axes.Single(a => a.Key == "momentum");
        momentum.Rules.Single(r => r.Key == "beats_sp500").Passed.Should().BeTrue();
    }

    [Fact]
    public async Task GetDossier_HeldCrypto_HidesAxesThatDoNotApply()
    {
        const string symbol = "SOLX";
        HoldPosition(symbol, quantity: 2m, valueUsd: 300m, costUsd: 200m, assetClass: "crypto", provider: "binance");
        StubHistory($"{symbol}-USD", "1y", [100m, 150m]);

        var body = await _client.GetFromJsonAsync<DossierStage2Shape>($"/api/v1/research/assets/{symbol}/dossier");

        body!.Checks!.Axes.Select(a => a.Key).Should().Equal("momentum", "fit");
        body.Performance!.LastClose.Should().Be(150m);
    }

    private void HoldPosition(
        string symbol, decimal quantity, decimal valueUsd, decimal costUsd,
        string assetClass = "equity", string provider = "ibkr")
    {
        var position = new BookFigurePosition(symbol, assetClass, quantity, costUsd, valueUsd, provider);
        var book = new BookFigures(10_000m - valueUsd, 0m, 10_000m - valueUsd, valueUsd, 10_000m, [position], false, []);
        _factory.BookFiguresMock
            .Setup(s => s.ReadAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(book);
    }

    private void StubHistory(string ticker, string rangeHint, IReadOnlyList<decimal> closes)
    {
        _ = rangeHint;
        var history = new PriceHistory("USD", closes.Select((c, i) => new DailyClose(FirstDay.AddDays(i), c)).ToList());
        _factory.PriceHistoryMock
            .Setup(s => s.GetAsync(ticker, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(history);
    }

    private sealed record ErrorShape(string Error, string ErrorCode);

    private sealed record ChartPointShape(DateOnly Date, decimal Close, decimal? Benchmark);

    private sealed record ChartTradeShape(DateOnly Date, string Side, decimal Quantity, decimal Price, string Currency);

    private sealed record ChartShape(
        string Symbol,
        string Range,
        string? Currency,
        List<ChartPointShape> Points,
        string BenchmarkLabel,
        List<ChartTradeShape> Trades,
        decimal? AverageCost,
        decimal? ChangePct,
        decimal? BenchmarkChangePct,
        string Source,
        DateTimeOffset RetrievedAt);

    private sealed record TargetShape(decimal TargetPrice, string? Currency, DateTimeOffset UpdatedAt);

    private sealed record ReturnsShape(decimal? UnrealizedUsd, decimal? RealizedUsd, decimal? TotalUsd, string Coverage);

    private sealed record PositionStage2Shape(decimal? ShareOfBookPercent, decimal? PositionCapPercent, ReturnsShape? Returns);

    private sealed record PerformanceShape(
        decimal LastClose, decimal? YearChangePct, decimal? FromHighPct, decimal? BenchmarkYearChangePct);

    private sealed record RuleShape(string Key, string Label, bool? Passed, decimal? Value, decimal? Threshold, string Unit);

    private sealed record AxisShape(string Key, string Label, int Passed, int Evaluated, List<RuleShape> Rules);

    private sealed record ChecksShape(List<AxisShape> Axes);

    private sealed record DossierStage2Shape(
        string Symbol,
        PositionStage2Shape? Position,
        PerformanceShape? Performance,
        ChecksShape? Checks,
        TargetShape? UserTarget);
}
