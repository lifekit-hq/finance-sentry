namespace FinanceSentry.Tests.Unit.Radar;

using FinanceSentry.Integration;
using FinanceSentry.Modules.Radar.Domain.Ports;
using FinanceSentry.Modules.Research.Domain.Ports;
using FluentAssertions;
using Moq;
using Xunit;

public class RadarIncomeOutlookAdapterTests
{
    private static readonly DateOnly From = new(2026, 11, 24);
    private static readonly DateOnly To = new(2027, 11, 24);

    private readonly Mock<IDividendYieldReader> _yields = new();
    private readonly Mock<IEarningsCalendarReader> _calendar = new();

    private RadarIncomeOutlookAdapter Sut() => new(_yields.Object, _calendar.Object);

    private void Yields(Dictionary<string, decimal?> latest) =>
        _yields.Setup(y => y.GetLatestAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(latest);

    private void Calendar(params EarningsCalendarItem[] items) =>
        _calendar.Setup(c => c.GetForTickersAsync(It.IsAny<IReadOnlyList<string>>(), From, To, It.IsAny<CancellationToken>()))
            .ReturnsAsync(items);

    [Fact]
    public async Task GetAsync_MergesTheYieldWithTheEarliestExDividendDate_AndIgnoresOtherEvents()
    {
        Yields(new() { ["AAPL"] = 0.005m, ["KO"] = null });
        Calendar(
            new EarningsCalendarItem("AAPL", "ex_dividend", new DateOnly(2027, 2, 9), false, "x"),
            new EarningsCalendarItem("AAPL", "ex_dividend", new DateOnly(2027, 1, 12), false, "x"),
            new EarningsCalendarItem("AAPL", "earnings", new DateOnly(2026, 12, 1), false, "x"),
            new EarningsCalendarItem("MSFT", "earnings", new DateOnly(2026, 12, 2), false, "x"),
            new EarningsCalendarItem("kO", "ex_dividend", new DateOnly(2027, 3, 1), true, "x"));

        var outlook = await Sut().GetAsync(["aapl", "KO", "MSFT", "NVDA"], From, To);

        outlook.Should().BeEquivalentTo(new Dictionary<string, IncomeOutlook>
        {
            ["AAPL"] = new(0.005m, new DateOnly(2027, 1, 12)),
            ["KO"] = new(null, new DateOnly(2027, 3, 1)),
        });
    }

    [Fact]
    public async Task GetAsync_KeepsTheYields_WhenTheCalendarProviderFails()
    {
        Yields(new() { ["AAPL"] = 0.005m });
        _calendar.Setup(c => c.GetForTickersAsync(It.IsAny<IReadOnlyList<string>>(), From, To, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new EarningsCalendarProviderException("down"));

        var outlook = await Sut().GetAsync(["AAPL"], From, To);

        outlook.Should().BeEquivalentTo(new Dictionary<string, IncomeOutlook> { ["AAPL"] = new(0.005m, null) });
    }

    [Fact]
    public async Task GetAsync_WithNoTickers_IsEmptyAndReadsNothing()
    {
        var outlook = await Sut().GetAsync([], From, To);

        outlook.Should().BeEmpty();
        _yields.VerifyNoOtherCalls();
        _calendar.VerifyNoOtherCalls();
    }
}
