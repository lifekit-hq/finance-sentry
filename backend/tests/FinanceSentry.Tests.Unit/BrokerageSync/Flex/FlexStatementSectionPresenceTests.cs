namespace FinanceSentry.Tests.Unit.BrokerageSync.Flex;

using FinanceSentry.Modules.BrokerageSync.Infrastructure.IBKR.Flex;
using FluentAssertions;
using Xunit;

/// <summary>
/// The holdings sync must tell "the query has no Open Positions / Cash Report section" (never wipe
/// holdings) from "the section is there and empty" (everything was sold). The real XML parser is the
/// contract here: it materialises absent collections as empty unless normalised.
/// </summary>
public class FlexStatementSectionPresenceTests
{
    private static FlexStatementXml Parse(string inner) => IbkrFlexClient.DeserializeStatement($"""
        <FlexQueryResponse queryName="SyntheticQuery" type="AF">
          <FlexStatements count="1">
            <FlexStatement accountId="U0000001" fromDate="20260101" toDate="20260101">{inner}</FlexStatement>
          </FlexStatements>
        </FlexQueryResponse>
        """).FlexStatements.Items[0];

    [Fact]
    public void AbsentSections_AreNull()
    {
        var statement = Parse(string.Empty);

        statement.OpenPositions.Should().BeNull();
        statement.CashReport.Should().BeNull();
    }

    [Fact]
    public void PresentButEmptySections_AreEmptyNotNull()
    {
        var statement = Parse("<OpenPositions /><CashReport />");

        statement.OpenPositions.Should().NotBeNull().And.BeEmpty();
        statement.CashReport.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void PopulatedSection_IsReadAndOtherSectionStaysAbsent()
    {
        var statement = Parse("""<OpenPositions><OpenPosition symbol="ZZZQ" position="1" levelOfDetail="SUMMARY" /></OpenPositions>""");

        statement.OpenPositions.Should().ContainSingle();
        statement.CashReport.Should().BeNull();
    }
}
