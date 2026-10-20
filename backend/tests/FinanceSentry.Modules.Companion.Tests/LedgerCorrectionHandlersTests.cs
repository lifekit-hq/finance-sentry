namespace FinanceSentry.Modules.Companion.Tests;

using System.Globalization;
using FinanceSentry.Modules.Companion.API.Responses;
using FinanceSentry.Modules.Companion.Application.Commands;
using FinanceSentry.Modules.Companion.Application.Queries;
using FinanceSentry.Modules.Companion.Domain;
using FinanceSentry.Modules.Companion.Domain.Repositories;
using FluentAssertions;
using Xunit;

/// <summary>The Ledger correction log: what is accepted on the way in, and the per-month review read.</summary>
public sealed class LedgerCorrectionHandlersTests
{
    private static readonly Guid User = Guid.NewGuid();

    private sealed class FakeRepository : ILedgerCorrectionRepository
    {
        public List<LedgerCorrection> Rows { get; } = [];

        public Task<LedgerCorrection> AddAsync(LedgerCorrection correction, CancellationToken ct = default)
        {
            correction.Id = Rows.Count + 1;
            Rows.Add(correction);
            return Task.FromResult(correction);
        }

        public Task<IReadOnlyList<LedgerCorrection>> ListAsync(
            Guid userId, DateTimeOffset from, DateTimeOffset to, int limit, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<LedgerCorrection>>(
                [.. Rows.Where(c => c.UserId == userId && c.CorrectedAt >= from && c.CorrectedAt < to)
                    .OrderByDescending(c => c.CorrectedAt).Take(limit)]);

        public Task<IReadOnlyList<DateTimeOffset>> ListCorrectedAtAsync(Guid userId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<DateTimeOffset>>([.. Rows.Where(c => c.UserId == userId).Select(c => c.CorrectedAt)]);
    }

    private static RecordLedgerCorrectionCommand Command(
        string agentSaid = "CPI prints on the 12th.",
        string whatWasWrong = "Wrong release date.",
        string ownerCorrection = "It is the 15th.",
        DateTimeOffset? correctedAt = null)
        => new(User, agentSaid, whatWasWrong, ownerCorrection, correctedAt);

    private static void Seed(FakeRepository repo, Guid user, params string[] correctedAt)
    {
        foreach (var at in correctedAt)
            repo.Rows.Add(new LedgerCorrection { Id = repo.Rows.Count + 1, UserId = user, AgentSaid = "a", WhatWasWrong = "w", OwnerCorrection = "o", CorrectedAt = DateTimeOffset.Parse(at, CultureInfo.InvariantCulture) });
    }

    [Fact]
    public async Task A_correction_is_saved_trimmed_with_its_time()
    {
        var repo = new FakeRepository();
        var at = new DateTimeOffset(2026, 10, 14, 11, 0, 0, TimeSpan.FromHours(1));

        var id = await new RecordLedgerCorrectionCommandHandler(repo).Handle(
            Command("  CPI prints on the 12th. ", "\tWrong release date.", "It is the 15th.\n", at), CancellationToken.None);

        id.Should().Be(1);
        var row = repo.Rows.Single();
        row.UserId.Should().Be(User);
        row.AgentSaid.Should().Be("CPI prints on the 12th.");
        row.WhatWasWrong.Should().Be("Wrong release date.");
        row.OwnerCorrection.Should().Be("It is the 15th.");
        row.CorrectedAt.Should().Be(at);
        row.CorrectedAt.Offset.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public async Task A_missing_time_means_now()
    {
        var repo = new FakeRepository();
        var before = DateTimeOffset.UtcNow;

        await new RecordLedgerCorrectionCommandHandler(repo).Handle(Command(), CancellationToken.None);

        repo.Rows.Single().CorrectedAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(DateTimeOffset.UtcNow);
    }

    [Theory]
    [InlineData("", "w", "o")]
    [InlineData("a", "   ", "o")]
    [InlineData("a", "w", "")]
    public async Task A_blank_field_is_refused(string agentSaid, string whatWasWrong, string ownerCorrection)
    {
        var repo = new FakeRepository();

        var id = await new RecordLedgerCorrectionCommandHandler(repo).Handle(
            Command(agentSaid, whatWasWrong, ownerCorrection), CancellationToken.None);

        id.Should().BeNull();
        repo.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task An_over_long_field_is_refused_and_one_at_the_limit_is_kept()
    {
        var repo = new FakeRepository();
        var handler = new RecordLedgerCorrectionCommandHandler(repo);

        (await handler.Handle(Command(whatWasWrong: new string('x', LedgerCorrectionLimits.WhatWasWrongMaxLength + 1)), CancellationToken.None))
            .Should().BeNull();
        (await handler.Handle(Command(agentSaid: new string('x', LedgerCorrectionLimits.AgentSaidMaxLength + 1)), CancellationToken.None))
            .Should().BeNull();
        (await handler.Handle(Command(ownerCorrection: new string('x', LedgerCorrectionLimits.OwnerCorrectionMaxLength)), CancellationToken.None))
            .Should().NotBeNull();

        repo.Rows.Should().ContainSingle();
    }

    [Fact]
    public async Task A_correction_time_in_the_future_is_refused()
    {
        var repo = new FakeRepository();

        var id = await new RecordLedgerCorrectionCommandHandler(repo).Handle(
            Command(correctedAt: DateTimeOffset.UtcNow.AddHours(1)), CancellationToken.None);

        id.Should().BeNull();
        repo.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task The_list_reads_one_utc_month_newest_first_with_counts_for_every_month()
    {
        var repo = new FakeRepository();
        Seed(repo, User, "2026-09-30T23:59:59Z", "2026-10-01T00:00:00Z", "2026-10-20T08:00:00Z", "2026-11-01T00:00:00Z");
        Seed(repo, Guid.NewGuid(), "2026-10-05T00:00:00Z");

        var result = await new ListLedgerCorrectionsQueryHandler(repo).Handle(
            new ListLedgerCorrectionsQuery(User, 2026, 10, 50), CancellationToken.None);

        result.Month.Should().Be("2026-10");
        result.Count.Should().Be(2);
        result.Items.Select(i => i.CorrectedAt.Day).Should().Equal(20, 1);
        result.ByMonth.Should().Equal(
            new LedgerCorrectionMonth("2026-11", 1),
            new LedgerCorrectionMonth("2026-10", 2),
            new LedgerCorrectionMonth("2026-09", 1));
    }

    [Fact]
    public async Task The_limit_caps_the_items_never_the_count()
    {
        var repo = new FakeRepository();
        Seed(repo, User, "2026-10-01T00:00:00Z", "2026-10-02T00:00:00Z", "2026-10-03T00:00:00Z");

        var result = await new ListLedgerCorrectionsQueryHandler(repo).Handle(
            new ListLedgerCorrectionsQuery(User, 2026, 10, 1), CancellationToken.None);

        result.Count.Should().Be(3);
        result.Items.Should().ContainSingle().Which.CorrectedAt.Day.Should().Be(3);
    }

    [Fact]
    public async Task An_empty_month_is_honest_emptiness()
    {
        var result = await new ListLedgerCorrectionsQueryHandler(new FakeRepository()).Handle(
            new ListLedgerCorrectionsQuery(User, 2026, 10, 50), CancellationToken.None);

        result.Should().BeEquivalentTo(new { Month = "2026-10", Count = 0, Items = Array.Empty<object>(), ByMonth = Array.Empty<object>() });
    }
}
