namespace FinanceSentry.Modules.Companion.Tests;

using FinanceSentry.Modules.Companion.Application.Services;
using FinanceSentry.Modules.Companion.Domain;
using FluentAssertions;
using Xunit;

/// <summary>Mode → disposition mapping (feature 031, US2, T024).</summary>
public sealed class DispositionTests
{
    private readonly MaterialityPolicy _policy = new();

    [Theory]
    [InlineData(NotificationMode.Quiet, EventDisposition.SuppressedByMode)]
    [InlineData(NotificationMode.Digest, EventDisposition.HeldForDigest)]
    [InlineData(NotificationMode.Scan, EventDisposition.Pending)]
    [InlineData(NotificationMode.Realtime, EventDisposition.Pending)]
    public void Mode_maps_to_disposition(NotificationMode mode, EventDisposition expected)
        => _policy.DispositionForMode(mode).Should().Be(expected);

    [Theory]
    [InlineData(NotificationMode.Realtime, null, EventDisposition.HeldForDigest)]
    [InlineData(NotificationMode.Realtime, 23.0, EventDisposition.HeldForDigest)]
    [InlineData(NotificationMode.Realtime, 24.0, EventDisposition.HeldForDigest)]
    [InlineData(NotificationMode.Realtime, 24.5, EventDisposition.Pending)]
    [InlineData(NotificationMode.Scan, null, EventDisposition.HeldForDigest)]
    [InlineData(NotificationMode.Scan, 48.0, EventDisposition.Pending)]
    [InlineData(NotificationMode.Digest, 48.0, EventDisposition.HeldForDigest)]
    [InlineData(NotificationMode.Quiet, null, EventDisposition.SuppressedByMode)]
    [InlineData(NotificationMode.Quiet, 48.0, EventDisposition.SuppressedByMode)]
    public void Sync_failure_rides_the_digest_unless_the_source_is_stale_past_a_day(
        NotificationMode mode, double? stalenessHours, EventDisposition expected)
    {
        TimeSpan? staleness = stalenessHours is { } h ? TimeSpan.FromHours(h) : null;

        _policy.DispositionFor(mode, CompanionEventKind.SyncFailure, staleness).Should().Be(expected);
    }

    [Theory]
    [InlineData(CompanionEventKind.ThesisBreak)]
    [InlineData(CompanionEventKind.LowBalance)]
    [InlineData(CompanionEventKind.AnalystAction)]
    public void Other_kinds_keep_the_mode_disposition_regardless_of_staleness(CompanionEventKind kind)
    {
        _policy.DispositionFor(NotificationMode.Realtime, kind).Should().Be(EventDisposition.Pending);
        _policy.DispositionFor(NotificationMode.Realtime, kind, TimeSpan.FromHours(1)).Should().Be(EventDisposition.Pending);
    }

    [Fact]
    public void Operational_failure_still_breaks_through_quiet()
        => _policy.DispositionFor(NotificationMode.Quiet, CompanionEventKind.OperationalFailure)
            .Should().Be(EventDisposition.Pending);

    [Fact]
    public void Escalation_age_is_one_day()
        => _policy.SyncFailureEscalationAge.Should().Be(TimeSpan.FromHours(24));
}
