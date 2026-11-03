namespace FinanceSentry.Tests.Unit.BankSync.Infrastructure;

using FinanceSentry.Core.Domain;
using FinanceSentry.Modules.BankSync.Infrastructure.Categorization;
using FluentAssertions;
using Xunit;
using FinanceSentry.Modules.BankSync.Application.Services.DetectionPhrases;

public class TransferDescriptionClassifierTests
{
    [Theory]
    [InlineData("To Mario Scalas", CategoryKeys.TransferOut)]
    [InlineData("To Alex Morgan", CategoryKeys.TransferOut)]
    [InlineData("To Interactive Brokers LLC", CategoryKeys.TransferOut)]
    [InlineData("To Instant Access Savings", CategoryKeys.TransferOut)]
    [InlineData("From Instant Access Savings", CategoryKeys.TransferIn)]
    [InlineData("From investment account", CategoryKeys.TransferIn)]
    // External incoming payments are income, NOT internal transfers — they must stay in cash-flow.
    [InlineData("Payment from Andrea Di Florio", CategoryKeys.Income)]
    [InlineData("Payment from Ss+c Wealth And Insurance", CategoryKeys.Income)]
    // Monobank «банка» (savings-jar) operations — charity MCC 8398, so the description is the
    // only reliable transfer signal. "Поповнення" = top-up (out), "З банки" = from jar (in).
    [InlineData("Поповнення «Поточний RUSORIZ»", CategoryKeys.TransferOut)]
    [InlineData("Поповнення «Секретний Русоріз 2.0»", CategoryKeys.TransferOut)]
    [InlineData("З банки «Поточний RUSORIZ»", CategoryKeys.TransferIn)]
    public void Resolve_DirectionalPrefix_ClassifiesTransfer(string description, string expected)
    {
        TransferDescriptionClassifier.Resolve(DetectionPhraseSet.Seed, description).Should().Be(expected);
    }

    [Theory]
    [InlineData("Tobacco Shop")]      // "To" without the trailing space must not match
    [InlineData("Tommy Hilfiger")]
    [InlineData("Lidl Ireland Ltd")]
    [InlineData("Fromage Cheese Co")] // "From" without the trailing space must not match
    [InlineData("")]
    [InlineData(null)]
    public void Resolve_NonTransfer_ReturnsNull(string? description)
    {
        TransferDescriptionClassifier.Resolve(DetectionPhraseSet.Seed, description).Should().BeNull();
    }

    [Fact]
    public void Resolve_LeadingWhitespace_IsTrimmedBeforeMatching()
    {
        TransferDescriptionClassifier.Resolve(DetectionPhraseSet.Seed, "   To Someone").Should().Be(CategoryKeys.TransferOut);
    }
}
