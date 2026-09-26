namespace FinanceSentry.Tests.Unit.BrokerageSync.Flex;

using FinanceSentry.Modules.BrokerageSync.Application.Services;
using FinanceSentry.Modules.BrokerageSync.Domain;
using FinanceSentry.Modules.BrokerageSync.Domain.Repositories;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.IBKR.Flex;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.Jobs;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

/// <summary>
/// fs-435 S5 PR2 — both the daily incremental sync job and the one-shot backfill job
/// must no-op cleanly (no sync call, no exception) when no user has an active Flex
/// credential, since supplying one is a manual step the human has not necessarily taken.
/// </summary>
public class IbkrFlexJobsTests
{
    [Fact]
    public async Task IncrementalSyncJob_NoActiveCredentials_NoOps_AndCallsSyncServiceNever()
    {
        var credentialRepo = new Mock<IIBKRFlexCredentialRepository>(MockBehavior.Strict);
        credentialRepo.Setup(r => r.GetAllActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<IBKRFlexCredential>)[]);
        var syncService = new Mock<IIbkrFlexTradeSyncService>(MockBehavior.Strict);

        var job = new IbkrFlexIncrementalSyncJob(
            credentialRepo.Object, syncService.Object, NullLogger<IbkrFlexIncrementalSyncJob>.Instance);

        await job.ExecuteAsync();

        syncService.Verify(
            s => s.SyncAsync(It.IsAny<Guid>(), It.IsAny<FlexStatementWindow?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task BackfillJob_NoActiveCredentials_NoOps_AndCallsSyncServiceNever()
    {
        var credentialRepo = new Mock<IIBKRFlexCredentialRepository>(MockBehavior.Strict);
        credentialRepo.Setup(r => r.GetAllActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<IBKRFlexCredential>)[]);
        var syncService = new Mock<IIbkrFlexTradeSyncService>(MockBehavior.Strict);

        var job = new IbkrFlexBackfillJob(
            credentialRepo.Object, syncService.Object, NullLogger<IbkrFlexBackfillJob>.Instance);

        await job.ExecuteAsync();

        syncService.Verify(
            s => s.SyncAsync(It.IsAny<Guid>(), It.IsAny<FlexStatementWindow?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task IncrementalSyncJob_ActiveCredential_SyncsOnce_WithShortLookbackWindow()
    {
        var userId = Guid.NewGuid();
        var credential = new IBKRFlexCredential(userId, "999999", [1], [2], [3], 1);
        var credentialRepo = new Mock<IIBKRFlexCredentialRepository>(MockBehavior.Strict);
        credentialRepo.Setup(r => r.GetAllActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<IBKRFlexCredential>)[credential]);
        var syncService = new Mock<IIbkrFlexTradeSyncService>(MockBehavior.Strict);
        syncService
            .Setup(s => s.SyncAsync(userId, It.IsAny<FlexStatementWindow?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IbkrFlexTradeSyncResult(0, 0));

        var job = new IbkrFlexIncrementalSyncJob(
            credentialRepo.Object, syncService.Object, NullLogger<IbkrFlexIncrementalSyncJob>.Instance);

        await job.ExecuteAsync();

        syncService.Verify(
            s => s.SyncAsync(userId, It.IsAny<FlexStatementWindow?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public void BuildYearlyWindows_WalksCalendarYearsFrom2022ToToday()
    {
        var asOf = new DateOnly(2026, 3, 15);

        var windows = IbkrFlexBackfillJob.BuildYearlyWindows(asOf);

        windows.Should().HaveCount(5); // 2022, 2023, 2024, 2025, 2026
        windows[0].FromDate.Should().Be(new DateOnly(2022, 1, 1));
        windows[0].ToDate.Should().Be(new DateOnly(2022, 12, 31));
        windows[3].FromDate.Should().Be(new DateOnly(2025, 1, 1));
        windows[3].ToDate.Should().Be(new DateOnly(2025, 12, 31));
        windows[^1].FromDate.Should().Be(new DateOnly(2026, 1, 1));
        windows[^1].ToDate.Should().Be(asOf, "the final window must stop at today, not run past it into 12-31");
    }

    [Fact]
    public void BuildYearlyWindows_EachWindowStaysWithinTheFlexApi365DayCap()
    {
        var windows = IbkrFlexBackfillJob.BuildYearlyWindows(new DateOnly(2026, 9, 26));

        windows.Should().OnlyContain(w => w.ToDate.DayNumber - w.FromDate.DayNumber <= 365);
    }
}
