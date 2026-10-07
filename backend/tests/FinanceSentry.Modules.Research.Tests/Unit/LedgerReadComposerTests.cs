namespace FinanceSentry.Modules.Research.Tests.Unit;

using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FluentAssertions;
using Xunit;

public class LedgerReadComposerTests
{
    private static AssetDossierResult Dossier(
        DossierPositionSection? position = null,
        IReadOnlyList<NewsArticleDto>? news = null,
        DateTimeOffset? generatedAt = null) =>
        new(
            Symbol: "AAPL",
            Position: position,
            Thesis: null,
            Valuation: null,
            Analysts: null,
            RecentNews: news ?? [],
            NextEarnings: null,
            RadarSignals: [],
            GeneratedAt: generatedAt ?? DateTimeOffset.UtcNow);

    [Fact]
    public void Fingerprint_IgnoresGeneratedAt()
    {
        // GeneratedAt moves on every dossier request; if it fed the digest every read would be stale.
        var a = Dossier(generatedAt: DateTimeOffset.UnixEpoch);
        var b = Dossier(generatedAt: DateTimeOffset.UnixEpoch.AddYears(3));

        LedgerReadComposer.Fingerprint(a).Should().Be(LedgerReadComposer.Fingerprint(b));
    }

    [Fact]
    public void Fingerprint_ChangesWhenPositionChanges()
    {
        var before = Dossier();
        var after = Dossier(new DossierPositionSection("ibkr", "Equities", 10m, 1500m, 1200m, 300m, 25m, []));

        LedgerReadComposer.Fingerprint(before).Should().NotBe(LedgerReadComposer.Fingerprint(after));
    }

    [Fact]
    public void Fingerprint_ChangesWhenNewsArrives()
    {
        var article = new NewsArticleDto(
            Guid.NewGuid(), "Reuters", "Headline", "https://x", null, ["AAPL"], [], DateTimeOffset.UtcNow);

        LedgerReadComposer.Fingerprint(Dossier())
            .Should().NotBe(LedgerReadComposer.Fingerprint(Dossier(news: [article])));
    }

    [Fact]
    public void Prompt_NamesMissingSectionsRatherThanOmittingThem()
    {
        // Crypto and thin-coverage tickers have no thesis/position; the agent must be told so
        // explicitly instead of being left to infer from silence.
        var prompt = LedgerReadComposer.Prompt(Dossier());

        prompt.Should().Contain("AAPL");
        prompt.Should().Contain("Position: not held.");
        prompt.Should().Contain("Thesis: none on file.");
    }

    [Fact]
    public void Prompt_IncludesPositionFiguresWhenHeld()
    {
        var prompt = LedgerReadComposer.Prompt(
            Dossier(new DossierPositionSection("ibkr", "Equities", 10m, 1500m, 1200m, 300m, 25m, [])));

        prompt.Should().Contain("ibkr");
        prompt.Should().Contain("1500");
        prompt.Should().Contain("1200");
    }
}

public class LedgerReadStalenessTests
{
    private static AssetLedgerRead Read(DateTimeOffset generatedAt, string fingerprint = "abc") =>
        new() { Narrative = "n", GeneratedAt = generatedAt, SourceFingerprint = fingerprint };

    [Fact]
    public void FreshReadWithMatchingFingerprint_IsNotStale()
    {
        LedgerReadStaleness.IsStale(Read(DateTimeOffset.UtcNow), "abc").Should().BeFalse();
    }

    [Fact]
    public void ReadOlderThanADay_IsStale()
    {
        var old = Read(DateTimeOffset.UtcNow - LedgerReadStaleness.MaxAge - TimeSpan.FromMinutes(1));
        LedgerReadStaleness.IsStale(old, "abc").Should().BeTrue();
    }

    [Fact]
    public void FingerprintMismatch_IsStale()
    {
        LedgerReadStaleness.IsStale(Read(DateTimeOffset.UtcNow), "moved").Should().BeTrue();
    }

    [Fact]
    public void UncomputableFingerprint_FallsBackToAgeOnly()
    {
        LedgerReadStaleness.IsStale(Read(DateTimeOffset.UtcNow), null).Should().BeFalse();
    }
}
