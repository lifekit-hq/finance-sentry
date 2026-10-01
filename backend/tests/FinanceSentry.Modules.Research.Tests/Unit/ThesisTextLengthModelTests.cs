namespace FinanceSentry.Modules.Research.Tests.Unit;

using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

/// <summary>
/// #443 Bug 1 regression, infrastructure-free half: a narrative thesis must not be capped at a
/// label-sized length. <c>ThesisTextColumnLimitTests</c> proves the same guarantee against a real
/// PostgreSQL instance, but it needs Docker and is skipped where none is reachable. These
/// assertions read EF Core's model metadata, so they run on every host and catch the realistic
/// regression vector — somebody shrinking the <c>HasMaxLength</c> in
/// <see cref="ResearchDbContext"/>.
/// </summary>
public sealed class ThesisTextLengthModelTests
{
    private const int ExpectedThesisTextMaxLength = 4000;

    /// <summary>The narrative that #443 reported as rejected was ~200 chars.</summary>
    private const int ReportedNarrativeLength = 200;

    private static IReadOnlyDictionary<string, IProperty> ThesisProperties()
    {
        // Model building never opens a connection, so a placeholder connection string is enough to
        // get the Npgsql-resolved relational metadata (column types) without a live database.
        var options = new DbContextOptionsBuilder<ResearchDbContext>()
            .UseNpgsql("Host=model-only;Database=model-only")
            .Options;

        using var ctx = new ResearchDbContext(options, NoCurrentUser.Instance);
        return ctx.Model.FindEntityType(typeof(InvestmentThesis))!
            .GetProperties()
            .ToDictionary(p => p.Name);
    }

    [Fact]
    public void ThesisText_IsSizedForANarrative_NotALabel()
    {
        var thesisText = ThesisProperties()[nameof(InvestmentThesis.ThesisText)];

        thesisText.GetMaxLength().Should().Be(ExpectedThesisTextMaxLength,
            "a thesis is a narrative — #443 was raised because save_thesis rejected text past ~60 chars");
        thesisText.GetMaxLength().Should().BeGreaterThan(ReportedNarrativeLength,
            "the narrative length reported in #443 must fit with room to spare");
        thesisText.IsNullable.Should().BeFalse("every thesis must carry its reasoning");
    }

    [Fact]
    public void ThesisText_MapsToAVarcharOfTheDeclaredLength()
    {
        var thesisText = ThesisProperties()[nameof(InvestmentThesis.ThesisText)];

        thesisText.GetColumnType().Should().Be($"character varying({ExpectedThesisTextMaxLength})",
            "the storage column, not just the model annotation, has to carry the full length");
    }

    [Fact]
    public void Ticker_StaysLabelSized_SoTheLimitsAreNotConfused()
    {
        var ticker = ThesisProperties()[nameof(InvestmentThesis.Ticker)];

        ticker.GetMaxLength().Should().Be(20,
            "widening the thesis narrative must not have widened the ticker column with it");
    }
}
