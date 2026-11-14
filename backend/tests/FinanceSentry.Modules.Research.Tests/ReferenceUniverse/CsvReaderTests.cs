namespace FinanceSentry.Modules.Research.Tests.ReferenceUniverse;

using FinanceSentry.Modules.Research.Application.Services.ReferenceUniverse;
using FinanceSentry.Modules.Research.Infrastructure.Sources.ReferenceUniverse;
using FluentAssertions;
using Xunit;

public sealed class CsvReaderTests
{
    private static List<List<string>> Read(string text) => CsvReader.ReadRows(new StringReader(text)).ToList();

    [Fact]
    public void ReadRows_SplitsPlainFieldsAndStripsTheByteOrderMark()
    {
        var rows = Read("﻿a,b,c\n1,2,3\n");

        rows.Should().BeEquivalentTo(
            new[] { new[] { "a", "b", "c" }, new[] { "1", "2", "3" } }, o => o.WithStrictOrdering());
    }

    [Fact]
    public void ReadRows_KeepsCommasQuotesAndNewlinesInsideQuotedFields()
    {
        var rows = Read("name,summary\r\n\"Acme, Inc.\",\"says \"\"hi\"\"\nthen leaves\"\r\nplain,x");

        rows.Should().HaveCount(3);
        rows[1].Should().Equal("Acme, Inc.", "says \"hi\"\nthen leaves");
        rows[2].Should().Equal("plain", "x");
    }

    [Fact]
    public void ReadRows_KeepsEmptyFieldsAndSkipsBlankLines()
    {
        var rows = Read("a,,c\n\n,,\nd,e,\"\"\n");

        rows.Should().BeEquivalentTo(
            new[] { new[] { "a", "", "c" }, new[] { "", "", "" }, new[] { "d", "e", "" } }, o => o.WithStrictOrdering());
    }

    [Fact]
    public void MapHeader_NamesTheMissingColumn()
    {
        var act = () => CsvReader.MapHeader(["symbol", "name"], "symbol", "sector");

        act.Should().Throw<ReferenceFeedException>().WithMessage("*'sector'*");
    }
}
