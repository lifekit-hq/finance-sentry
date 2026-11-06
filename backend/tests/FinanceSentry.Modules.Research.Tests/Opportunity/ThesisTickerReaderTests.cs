namespace FinanceSentry.Modules.Research.Tests.Opportunity;

using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Repositories;
using FluentAssertions;
using Moq;
using Xunit;

/// <summary>
/// #1322: the Radar universe reads thesis tickers per person. Oracle: the Radar universe contract
/// (one entry per distinct upper-case symbol, only the asked person's theses). A reader that ignored
/// the user id, or returned raw tickers, would widen or duplicate the universe.
/// </summary>
public sealed class ThesisTickerReaderTests
{
    [Fact]
    public async Task ListTickersAsync_ReturnsOnlyTheAskedPersonsTheses()
    {
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        var repo = new Mock<IThesisRepository>();
        repo.Setup(r => r.ListUnscopedAsync(userA, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        repo.Setup(r => r.ListUnscopedAsync(userB, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new InvestmentThesis { UserId = userB, Ticker = "X", ThesisText = "t" }]);
        var reader = new ThesisTickerReader(repo.Object);

        (await reader.ListTickersAsync(userA)).Should().BeEmpty();
        (await reader.ListTickersAsync(userB)).Should().Equal("X");
    }

    [Fact]
    public async Task ListTickersAsync_TrimsUpperCasesAndDeDuplicatesAndDropsBlanks()
    {
        var user = Guid.NewGuid();
        var repo = new Mock<IThesisRepository>();
        repo.Setup(r => r.ListUnscopedAsync(user, It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            new InvestmentThesis { UserId = user, Ticker = " tsm ", ThesisText = "t" },
            new InvestmentThesis { UserId = user, Ticker = "TSM", ThesisText = "t" },
            new InvestmentThesis { UserId = user, Ticker = "   ", ThesisText = "t" },
            new InvestmentThesis { UserId = user, Ticker = "nvda", ThesisText = "t" },
        ]);

        var tickers = await new ThesisTickerReader(repo.Object).ListTickersAsync(user);

        tickers.Should().BeEquivalentTo("TSM", "NVDA");
        tickers.Should().HaveCount(2);
    }
}
