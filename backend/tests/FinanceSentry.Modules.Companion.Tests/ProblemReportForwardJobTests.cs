namespace FinanceSentry.Modules.Companion.Tests;

using FinanceSentry.Modules.Companion.Application.Services;
using FinanceSentry.Modules.Companion.Domain;
using FinanceSentry.Modules.Companion.Domain.Repositories;
using FinanceSentry.Modules.Companion.Infrastructure.Jobs;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

/// <summary>
/// The forwarder: reports stay pending (quietly) while the relay is not set up, a retry carries the same request id and
/// body so the relay's dedup makes it one note, a forwarded report is never sent again, and a stuck report ends Failed.
/// </summary>
public sealed class ProblemReportForwardJobTests
{
    private sealed class FakeRepository : IProblemReportRepository
    {
        public List<ProblemReport> Rows { get; } = [];

        public int Updates { get; private set; }

        public Task<ProblemReport> AddAsync(ProblemReport report, CancellationToken ct = default)
        {
            report.Id = Rows.Count + 1;
            Rows.Add(report);
            return Task.FromResult(report);
        }

        public Task<IReadOnlyList<DateTimeOffset>> ListCreatedAtSinceAsync(Guid userId, DateTimeOffset since, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<DateTimeOffset>>(
                [.. Rows.Where(r => r.UserId == userId && r.CreatedAt >= since).Select(r => r.CreatedAt).Order()]);

        public Task<IReadOnlyList<ProblemReport>> ListDueUnscopedAsync(DateTimeOffset now, int limit, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ProblemReport>>(
                [.. Rows.Where(r => r.Status == ProblemReportStatus.Pending && (r.NextAttemptAt is null || r.NextAttemptAt <= now))
                    .OrderBy(r => r.Id).Take(limit)]);

        public Task UpdateUnscopedAsync(ProblemReport report, CancellationToken ct = default)
        {
            Updates++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeRelay(bool configured = true) : IProblemReportRelay
    {
        public Queue<RelayResult> Results { get; } = new();

        public List<(string RequestId, string Body)> Sent { get; } = [];

        public bool IsConfigured { get; } = configured;

        public Task<RelayResult> SendAsync(string requestId, string body, CancellationToken ct = default)
        {
            Sent.Add((requestId, body));
            return Task.FromResult(Results.Count > 0 ? Results.Dequeue() : new RelayResult(RelayStatus.Sent));
        }
    }

    private static ProblemReport NewReport(Guid? userId = null) => new()
    {
        UserId = userId ?? Guid.NewGuid(),
        Role = "member",
        Kind = ProblemReportKind.Broken,
        Text = "it is broken",
        RoutePattern = "/accounts/:id",
        AppVersion = "1.15.0",
        Device = ProblemReportDevice.Phone,
        Client = "iOS Safari",
        CorrelationId = "corr",
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static ProblemReportForwardJob Job(FakeRepository repo, FakeRelay relay)
        => new(repo, relay, NullLogger<ProblemReportForwardJob>.Instance);

    private static async Task<FakeRepository> RepoWithAsync(params ProblemReport[] reports)
    {
        var repo = new FakeRepository();
        foreach (var r in reports)
            await repo.AddAsync(r);
        return repo;
    }

    [Fact]
    public async Task With_no_relay_configured_reports_stay_pending_and_nothing_is_touched()
    {
        var report = NewReport();
        var repo = await RepoWithAsync(report);
        var relay = new FakeRelay(configured: false);

        await Job(repo, relay).ExecuteAsync();

        relay.Sent.Should().BeEmpty();
        repo.Updates.Should().Be(0);
        report.Status.Should().Be(ProblemReportStatus.Pending);
        report.Attempts.Should().Be(0, "an unconfigured relay is not a failed attempt");
        report.NextAttemptAt.Should().BeNull();
    }

    [Fact]
    public async Task A_sent_report_is_marked_forwarded_and_never_sent_again()
    {
        var report = NewReport();
        var repo = await RepoWithAsync(report);
        var relay = new FakeRelay();
        var job = Job(repo, relay);

        await job.ExecuteAsync();
        await job.ExecuteAsync();

        relay.Sent.Should().ContainSingle().Which.RequestId.Should().Be("fs-report-1");
        report.Status.Should().Be(ProblemReportStatus.Forwarded);
        report.ForwardedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task A_retry_after_a_failure_sends_the_same_request_id_and_body()
    {
        var report = NewReport();
        var repo = await RepoWithAsync(report);
        var relay = new FakeRelay();
        relay.Results.Enqueue(new RelayResult(RelayStatus.Failed, "relay exit 255: connection refused"));
        var job = Job(repo, relay);

        await job.ExecuteAsync();

        report.Status.Should().Be(ProblemReportStatus.Pending);
        report.Attempts.Should().Be(1);
        report.LastError.Should().Contain("connection refused");
        report.NextAttemptAt.Should().BeCloseTo(DateTimeOffset.UtcNow + ProblemReportLimits.ForwardBackoff[0], TimeSpan.FromSeconds(5));

        await job.ExecuteAsync();
        relay.Sent.Should().ContainSingle("the report is not due again until its backoff passes");

        report.NextAttemptAt = DateTimeOffset.UtcNow.AddSeconds(-1);
        await job.ExecuteAsync();

        relay.Sent.Should().HaveCount(2);
        relay.Sent[1].Should().Be(relay.Sent[0], "the relay dedups on the request id, so a repeat is the same note");
        report.Status.Should().Be(ProblemReportStatus.Forwarded);
        report.LastError.Should().BeNull();
    }

    [Fact]
    public async Task A_relay_rate_limit_defers_the_batch_without_counting_an_attempt()
    {
        var first = NewReport();
        var second = NewReport();
        var repo = await RepoWithAsync(first, second);
        var relay = new FakeRelay();
        relay.Results.Enqueue(new RelayResult(RelayStatus.Deferred, "rate limit"));

        await Job(repo, relay).ExecuteAsync();

        relay.Sent.Should().ContainSingle("the rest of the batch waits for the next run");
        first.Attempts.Should().Be(0);
        first.Status.Should().Be(ProblemReportStatus.Pending);
        second.Status.Should().Be(ProblemReportStatus.Pending);
    }

    [Fact]
    public async Task A_report_that_keeps_failing_ends_failed_after_the_attempt_cap()
    {
        var report = NewReport();
        var repo = await RepoWithAsync(report);
        var relay = new FakeRelay();
        for (var i = 0; i < ProblemReportLimits.MaxForwardAttempts; i++)
            relay.Results.Enqueue(new RelayResult(RelayStatus.Failed, "down"));
        var job = Job(repo, relay);

        for (var i = 0; i < ProblemReportLimits.MaxForwardAttempts; i++)
        {
            report.NextAttemptAt = null;
            await job.ExecuteAsync();
        }

        report.Status.Should().Be(ProblemReportStatus.Failed);
        report.Attempts.Should().Be(ProblemReportLimits.MaxForwardAttempts);
        report.NextAttemptAt.Should().BeNull();

        await job.ExecuteAsync();
        relay.Sent.Should().HaveCount(ProblemReportLimits.MaxForwardAttempts, "a failed report is not picked up again");
    }

    [Fact]
    public async Task One_failing_report_does_not_hold_back_the_next()
    {
        var first = NewReport();
        var second = NewReport();
        var repo = await RepoWithAsync(first, second);
        var relay = new FakeRelay();
        relay.Results.Enqueue(new RelayResult(RelayStatus.Failed, "bad body"));

        await Job(repo, relay).ExecuteAsync();

        first.Status.Should().Be(ProblemReportStatus.Pending);
        second.Status.Should().Be(ProblemReportStatus.Forwarded);
    }
}
