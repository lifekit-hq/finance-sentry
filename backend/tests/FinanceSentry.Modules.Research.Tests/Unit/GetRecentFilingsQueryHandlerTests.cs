namespace FinanceSentry.Modules.Research.Tests.Unit;

using FinanceSentry.Modules.Research.Application.Queries;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Exceptions;
using FinanceSentry.Modules.Research.Domain.Ports;
using FluentAssertions;
using Moq;
using Xunit;

/// <summary>
/// #1375: <c>get_recent_filings</c> / <c>GET research/filings/{ticker}</c> fail with a distinct code when EDGAR cannot be
/// used, instead of returning an empty list a caller would read as "no filings".
/// </summary>
public sealed class GetRecentFilingsQueryHandlerTests
{
    private static readonly GetRecentFilingsQuery Query = new("MU", null, 10);

    [Fact]
    public async Task Filings_AreMappedToTheDto()
    {
        var svc = new Mock<ISecEdgarService>();
        svc.Setup(s => s.GetRecentFilingsAsync("MU", null, 10, It.IsAny<CancellationToken>(), true))
            .ReturnsAsync([new EdgarFiling(
                "MU", "10-Q", new DateOnly(2026, 9, 30), new DateOnly(2026, 8, 28), "10-Q", "0000723125-26-000001",
                "https://www.sec.gov/x", true)]);

        var result = await new GetRecentFilingsQueryHandler(svc.Object).Handle(Query, CancellationToken.None);

        result.Should().ContainSingle().Which.Should().Match<FinanceSentry.Modules.Research.API.Responses.EdgarFilingDto>(
            f => f.Ticker == "MU" && f.Form == "10-Q" && f.AccessionNumber == "0000723125-26-000001");
    }

    [Fact]
    public async Task NotAFiler_StaysAnEmptyList()
    {
        var svc = new Mock<ISecEdgarService>();
        svc.Setup(s => s.GetRecentFilingsAsync("MU", null, 10, It.IsAny<CancellationToken>(), true))
            .ReturnsAsync([]);

        var result = await new GetRecentFilingsQueryHandler(svc.Object).Handle(Query, CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Theory]
    [InlineData(EdgarErrorCodes.MapUnavailable)]
    [InlineData(EdgarErrorCodes.SourceUnavailable)]
    public async Task ProviderFailure_IsA503WithTheDistinctCode_NotAnEmptyList(string code)
    {
        var svc = new Mock<ISecEdgarService>();
        svc.Setup(s => s.GetRecentFilingsAsync("MU", null, 10, It.IsAny<CancellationToken>(), true))
            .ThrowsAsync(new EdgarProviderException("EDGAR ticker->CIK map unavailable; cannot resolve MU.", code));

        var act = () => new GetRecentFilingsQueryHandler(svc.Object).Handle(Query, CancellationToken.None);

        var thrown = (await act.Should().ThrowAsync<EdgarSourceUnavailableException>()).Which;
        thrown.ErrorCode.Should().Be(code);
        thrown.StatusCode.Should().Be(503);
        thrown.Message.Should().StartWith($"[{code}]");
    }
}
