namespace FinanceSentry.Modules.Research.Tests.Unit;

using FinanceSentry.Modules.Research.Application.Queries;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Repositories;
using FluentAssertions;
using Moq;
using Xunit;

/// <summary>A bounded news query (Since + Until) reads the date range; an open one keeps the newest-first read.</summary>
public class GetNewsForTickerQueryHandlerTests
{
    private static readonly DateTimeOffset From = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Until = From.AddDays(2);

    [Fact]
    public async Task Since_and_until_read_the_date_range()
    {
        var article = new NewsArticle { Title = "in range", PublishedAt = From.AddHours(5), Tickers = ["MU"] };
        var repo = new Mock<INewsRepository>(MockBehavior.Strict);
        repo.Setup(r => r.GetForTickerBetweenAsync("MU", From, Until, 40, It.IsAny<CancellationToken>()))
            .ReturnsAsync([article]);

        var result = await new GetNewsForTickerQueryHandler(repo.Object)
            .Handle(new GetNewsForTickerQuery("MU", From, 40, Until), CancellationToken.None);

        result.Select(a => a.Title).Should().Equal("in range");
        repo.VerifyAll();
    }

    [Fact]
    public async Task Since_alone_keeps_the_newest_first_read()
    {
        var repo = new Mock<INewsRepository>(MockBehavior.Strict);
        repo.Setup(r => r.GetForTickerAsync("MU", From, 40, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await new GetNewsForTickerQueryHandler(repo.Object)
            .Handle(new GetNewsForTickerQuery("MU", From, 40), CancellationToken.None);

        result.Should().BeEmpty();
        repo.VerifyAll();
    }
}
