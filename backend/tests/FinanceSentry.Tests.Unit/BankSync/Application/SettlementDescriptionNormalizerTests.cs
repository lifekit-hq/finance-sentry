namespace FinanceSentry.Tests.Unit.BankSync.Application;

using FinanceSentry.Modules.BankSync.Application.Services;
using FluentAssertions;
using Xunit;

public class SettlementDescriptionNormalizerTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ReturnsEmpty_ForMissingDescription(string? description)
    {
        SettlementDescriptionNormalizer.Normalize(description).Should().BeEmpty();
    }

    [Fact]
    public void StripsTheSettlementStamp()
    {
        SettlementDescriptionNormalizer
            .Normalize("*MOBI DENYS SYCHOV IE26090266947650 TxnDate: 02Sep2026 *MOBI DENYS SYCHOV")
            .Should().Be("*mobi denys sychov ie26090266947650 *mobi denys sychov");
    }

    [Fact]
    public void PendingAndPostedFormsOfOneMovementNormalizeAlike()
    {
        var pending = SettlementDescriptionNormalizer
            .Normalize("Denys Sychov IE26082062813884 Sent from Revolut");
        var posted = SettlementDescriptionNormalizer
            .Normalize("DENYS SYCHOV IE26082062813884 TxnDate: 20Aug2026 Sent from Revolut");

        posted.Should().Be(pending);
    }

    [Fact]
    public void StripsAStampWithNoSpaceAfterTheColon()
    {
        SettlementDescriptionNormalizer.Normalize("ACME LTD TxnDate:5Jan2026")
            .Should().Be("acme ltd");
    }

    [Fact]
    public void CollapsesWhitespaceAndTrims()
    {
        SettlementDescriptionNormalizer.Normalize("  Lidl   Ireland\tLtd  ")
            .Should().Be("lidl ireland ltd");
    }

    [Theory]
    // A merchant whose name merely starts with the same letters is not a stamp.
    [InlineData("TxnDateWorks Ltd", "txndateworks ltd")]
    // No date after the label — leave it alone rather than guess.
    [InlineData("Payment TxnDate: pending", "payment txndate: pending")]
    // A bare date is a merchant reference, not the provider's stamp.
    [InlineData("REF 02Sep2026 Acme", "ref 02sep2026 acme")]
    public void LeavesNonStampTextIntact(string description, string expected)
    {
        SettlementDescriptionNormalizer.Normalize(description).Should().Be(expected);
    }

    [Fact]
    public void KeepsDistinctMerchantsDistinct()
    {
        var a = SettlementDescriptionNormalizer.Normalize("*MOBI TOP-UP 0857860057 TxnDate: 20Aug2026");
        var b = SettlementDescriptionNormalizer.Normalize("DENYS SYCHOV IE26082062813884 Sent from Revolut");

        a.Should().NotBe(b);
    }
}
