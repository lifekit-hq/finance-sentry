namespace FinanceSentry.Modules.Research.Tests.Persistence;

using FinanceSentry.Modules.Research.Application.Commands;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

/// <summary>
/// #443 Bug 2: <c>save_thesis</c> silently lost everything past a label-sized prefix. These tests
/// drive the production write path — <see cref="SaveThesisCommandHandler"/> over the real
/// <see cref="ThesisRepository"/> — and then read the thesis back through that same repository on
/// a *separate* context, so nothing can be served from the change tracker.
/// The store is SQLite (<see cref="ThesisSqliteFixture"/>): real SQL, real
/// parameter binding, real materialisation, and no Docker — which is what lets this run on every
/// host, unlike the Postgres-backed <c>ThesisTextColumnLimitTests</c>.
/// </summary>
public sealed class ThesisTextRoundTripTests
{
    /// <summary>The width #443 settled on for the thesis narrative column.</summary>
    private const int ThesisTextLimit = 4000;

    /// <summary>The narrative length #443 reported as rejected.</summary>
    private const int ReportedNarrativeLength = 200;

    private const string Ticker = "MU";

    private static string Narrative(int length)
    {
        const string Sentence = "Micron memory cycle recovery remains intact through pricing discipline. ";
        return string.Concat(Enumerable.Repeat(Sentence, (length / Sentence.Length) + 1))[..length];
    }

    private static async Task<InvestmentThesis> SaveAndReadBackAsync(
        ThesisSqliteFixture fixture, Guid userId, string thesisText)
    {
        await using (var writeCtx = fixture.CreateContext())
        {
            var handler = new SaveThesisCommandHandler(
                new ThesisRepository(writeCtx),
                new RecordingThesisEventRecorder(),
                NullLogger<SaveThesisCommandHandler>.Instance);

            await handler.Handle(
                new SaveThesisCommand(
                    userId, null, Ticker, thesisText,
                    KeyDataPoints: [],
                    Catalysts: [],
                    InvalidationTriggers: []),
                CancellationToken.None);
        }

        // FindByTickerAsync rather than ListAsync: both are production read paths, but ListAsync
        // orders by a DateTimeOffset, which the SQLite provider refuses to translate.
        await using var readCtx = fixture.CreateContext();
        var found = await new ThesisRepository(readCtx)
            .FindByTickerAsync(userId, Ticker, CancellationToken.None);

        return found.Should().ContainSingle().Subject;
    }

    [Fact]
    public async Task ThesisText_AtTheColumnLimit_SurvivesSaveAndReadBack()
    {
        await using var fixture = await ThesisSqliteFixture.CreateAsync();
        var thesisText = Narrative(ThesisTextLimit);

        var loaded = await SaveAndReadBackAsync(fixture, Guid.NewGuid(), thesisText);

        loaded.ThesisText.Should().Be(thesisText,
            "a thesis at the column limit must come back out of the database byte-for-byte");
        loaded.ThesisText.Length.Should().Be(ThesisTextLimit);
    }

    [Fact]
    public async Task ThesisText_AtTheLengthReportedInIssue443_SurvivesSaveAndReadBack()
    {
        await using var fixture = await ThesisSqliteFixture.CreateAsync();
        var thesisText = Narrative(ReportedNarrativeLength);

        var loaded = await SaveAndReadBackAsync(fixture, Guid.NewGuid(), thesisText);

        loaded.ThesisText.Should().Be(thesisText,
            "#443 was raised because a narrative of roughly this length came back truncated");
    }

    [Fact]
    public async Task ThesisText_PastTheColumnLimit_IsRefusedByTheStorageLayer()
    {
        await using var fixture = await ThesisSqliteFixture.CreateAsync();

        var save = async () => await SaveAndReadBackAsync(
            fixture, Guid.NewGuid(), Narrative(ThesisTextLimit + 1));

        // Proves the limit under test is the one the production model declares, not a number this
        // test made up: the fixture derives the constraint from ResearchDbContext's HasMaxLength.
        // Shrinking that back towards the #443 cap makes the 4000-char test above fail here too.
        await save.Should().ThrowAsync<DbUpdateException>(
            "the column must refuse over-length text rather than silently truncating it, which is "
            + "the failure mode #443 reported");
    }
}
