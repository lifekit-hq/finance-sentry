namespace FinanceSentry.Modules.Companion.Tests;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Companion.Application.Commands;
using FinanceSentry.Modules.Companion.Domain;
using FinanceSentry.Modules.Companion.Domain.Exceptions;
using FinanceSentry.Modules.Companion.Domain.Repositories;
using FluentAssertions;
using Xunit;

/// <summary>Saving a problem report: Logto link required, the hourly and daily caps, and cleaning on the way in.</summary>
public sealed class SubmitProblemReportCommandTests
{
    private static readonly Guid User = Guid.NewGuid();

    private sealed class FakeRepository : IProblemReportRepository
    {
        public List<ProblemReport> Rows { get; } = [];

        public Task<ProblemReport> AddAsync(ProblemReport report, CancellationToken ct = default)
        {
            report.Id = Rows.Count + 1;
            report.CreatedAt = DateTimeOffset.UtcNow;
            Rows.Add(report);
            return Task.FromResult(report);
        }

        public Task<IReadOnlyList<DateTimeOffset>> ListCreatedAtSinceAsync(Guid userId, DateTimeOffset since, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<DateTimeOffset>>(
                [.. Rows.Where(r => r.UserId == userId && r.CreatedAt >= since).Select(r => r.CreatedAt).Order()]);

        public Task<IReadOnlyList<ProblemReport>> ListDueUnscopedAsync(DateTimeOffset now, int limit, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ProblemReport>>([]);

        public Task UpdateUnscopedAsync(ProblemReport report, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeLinks(bool linked) : IOrgIdentityLinkReader
    {
        public Task<bool> IsLinkedAsync(Guid userId, CancellationToken ct = default) => Task.FromResult(linked);
    }

    private static SubmitProblemReportCommand Command(string? text = "it is broken", string? route = "/accounts/:id") => new(
        User, "member", ProblemReportKind.Broken, text, route, "1.15.0", ProblemReportDevice.Phone, "iOS", "Safari", "corr-1");

    private static SubmitProblemReportCommandHandler Handler(FakeRepository repo, bool linked = true)
        => new(repo, new FakeLinks(linked));

    private static void Seed(FakeRepository repo, int count, TimeSpan age, Guid? user = null)
    {
        for (var i = 0; i < count; i++)
            repo.Rows.Add(new ProblemReport { Id = 1000 + repo.Rows.Count, UserId = user ?? User, CreatedAt = DateTimeOffset.UtcNow - age });
    }

    [Fact]
    public async Task A_linked_person_gets_a_reference_and_a_cleaned_pending_row()
    {
        var repo = new FakeRepository();

        var result = await Handler(repo).Handle(
            Command("my balance is $4,200 and account 12345678", "/accounts/:id?token=secret"), default);

        result.Reference.Should().Be($"FS-R-{result.Id}");
        var row = repo.Rows.Single();
        row.UserId.Should().Be(User);
        row.Role.Should().Be("member");
        row.Status.Should().Be(ProblemReportStatus.Pending);
        row.Text.Should().Be("my balance is [number removed] and account [number removed]");
        row.RoutePattern.Should().Be("/accounts/:id");
        row.Client.Should().Be("iOS Safari");
    }

    [Fact]
    public async Task A_person_without_a_logto_link_is_refused_and_nothing_is_saved()
    {
        var repo = new FakeRepository();

        var act = () => Handler(repo, linked: false).Handle(Command(), default);

        (await act.Should().ThrowAsync<ProblemReportNotAllowedException>()).Which.StatusCode.Should().Be(403);
        repo.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task The_sixth_report_within_an_hour_is_refused_with_a_retry_after()
    {
        var repo = new FakeRepository();
        Seed(repo, ProblemReportLimits.PerHour, TimeSpan.FromMinutes(20));

        var act = () => Handler(repo).Handle(Command(), default);

        var thrown = (await act.Should().ThrowAsync<ProblemReportRateLimitedException>()).Which;
        thrown.StatusCode.Should().Be(429);
        thrown.RetryAfterSeconds.Should().BeInRange(39 * 60, 40 * 60);
        repo.Rows.Should().HaveCount(ProblemReportLimits.PerHour);
    }

    [Fact]
    public async Task Reports_older_than_an_hour_do_not_count_toward_the_hourly_cap()
    {
        var repo = new FakeRepository();
        Seed(repo, ProblemReportLimits.PerHour, TimeSpan.FromMinutes(90));

        await Handler(repo).Handle(Command(), default);

        repo.Rows.Should().HaveCount(ProblemReportLimits.PerHour + 1);
    }

    [Fact]
    public async Task The_twenty_first_report_within_a_day_is_refused()
    {
        var repo = new FakeRepository();
        Seed(repo, ProblemReportLimits.PerDay, TimeSpan.FromHours(5));

        var act = () => Handler(repo).Handle(Command(), default);

        var thrown = (await act.Should().ThrowAsync<ProblemReportRateLimitedException>()).Which;
        thrown.RetryAfterSeconds.Should().BeInRange(18 * 3600, 19 * 3600);
    }

    [Fact]
    public async Task Another_persons_reports_do_not_count_against_the_caller()
    {
        var repo = new FakeRepository();
        Seed(repo, ProblemReportLimits.PerDay, TimeSpan.FromMinutes(5), user: Guid.NewGuid());

        await Handler(repo).Handle(Command(), default);

        repo.Rows.Should().HaveCount(ProblemReportLimits.PerDay + 1);
    }
}
