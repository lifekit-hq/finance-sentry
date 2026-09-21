namespace FinanceSentry.Tests.Unit.Alerts;

using System.Reflection;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Alerts.Application.Services;
using FinanceSentry.Modules.Alerts.Domain;
using FinanceSentry.Modules.Alerts.Domain.Repositories;
using FluentAssertions;
using Moq;
using Xunit;

public class AlertGeneratorServiceTests
{
    private readonly Mock<IAlertRepository> _repo = new();
    private readonly AlertGeneratorService _service;
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _accountId = Guid.NewGuid();

    public AlertGeneratorServiceTests()
    {
        _service = new AlertGeneratorService(_repo.Object);
    }

    [Fact]
    public async Task GenerateLowBalance_NoExisting_AddsAlert()
    {
        _repo.Setup(r => r.FindActiveAsync(_userId, AlertType.LowBalance, _accountId, default))
            .ReturnsAsync((Alert?)null);
        _repo.Setup(r => r.HasRecentAsync(
                _userId, AlertType.LowBalance, _accountId, "Chase", It.IsAny<DateTimeOffset>(), default))
            .ReturnsAsync(false);

        await _service.GenerateLowBalanceAlertAsync(_userId, _accountId, "Chase", 100m, 500m);

        _repo.Verify(r => r.AddAsync(It.Is<Alert>(a =>
            a.Type == AlertType.LowBalance &&
            a.Severity == AlertSeverity.Warning &&
            a.UserId == _userId &&
            a.ReferenceId == _accountId), default), Times.Once);
    }

    [Fact]
    public async Task GenerateLowBalance_ExistingActive_SkipsCreation()
    {
        _repo.Setup(r => r.FindActiveAsync(_userId, AlertType.LowBalance, _accountId, default))
            .ReturnsAsync(new Alert { Id = Guid.NewGuid() });

        await _service.GenerateLowBalanceAlertAsync(_userId, _accountId, "Chase", 100m, 500m);

        _repo.Verify(r => r.AddAsync(It.IsAny<Alert>(), default), Times.Never);
        _repo.Verify(
            r => r.HasRecentAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<string?>(),
                It.IsAny<DateTimeOffset>(), default),
            Times.Never);
    }

    [Fact]
    public async Task GenerateLowBalance_RecentDismissed_SkipsCreation()
    {
        _repo.Setup(r => r.FindActiveAsync(_userId, AlertType.LowBalance, _accountId, default))
            .ReturnsAsync((Alert?)null);
        _repo.Setup(r => r.HasRecentAsync(
                _userId, AlertType.LowBalance, _accountId, "Chase", It.IsAny<DateTimeOffset>(), default))
            .ReturnsAsync(true);

        await _service.GenerateLowBalanceAlertAsync(_userId, _accountId, "Chase", 100m, 500m);

        _repo.Verify(r => r.AddAsync(It.IsAny<Alert>(), default), Times.Never);
    }

    [Fact]
    public async Task ResolveLowBalance_ExistingActive_CallsResolve()
    {
        var existing = new Alert { Id = Guid.NewGuid() };
        _repo.Setup(r => r.FindActiveAsync(_userId, AlertType.LowBalance, _accountId, default))
            .ReturnsAsync(existing);

        await _service.ResolveLowBalanceAlertAsync(_userId, _accountId);

        _repo.Verify(r => r.ResolveAsync(existing.Id, default), Times.Once);
    }

    [Fact]
    public async Task ResolveLowBalance_NoExisting_DoesNothing()
    {
        _repo.Setup(r => r.FindActiveAsync(_userId, AlertType.LowBalance, _accountId, default))
            .ReturnsAsync((Alert?)null);

        await _service.ResolveLowBalanceAlertAsync(_userId, _accountId);

        _repo.Verify(r => r.ResolveAsync(It.IsAny<Guid>(), default), Times.Never);
    }

    [Fact]
    public async Task GenerateSyncFailure_NoExisting_AddsErrorAlert()
    {
        _repo.Setup(r => r.FindActiveAsync(_userId, AlertType.SyncFailure, _accountId, default))
            .ReturnsAsync((Alert?)null);
        _repo.Setup(r => r.HasRecentAsync(
                _userId, AlertType.SyncFailure, _accountId, "Chase", It.IsAny<DateTimeOffset>(), default))
            .ReturnsAsync(false);

        await _service.GenerateSyncFailureAlertAsync(_userId, "truelayer", _accountId, "Chase", "ITEM_LOGIN_REQUIRED");

        _repo.Verify(r => r.AddAsync(It.Is<Alert>(a =>
            a.Type == AlertType.SyncFailure &&
            a.Severity == AlertSeverity.Error), default), Times.Once);
    }

    [Fact]
    public async Task ResolveSyncFailure_ExistingActive_CallsResolve()
    {
        var existing = new Alert { Id = Guid.NewGuid() };
        _repo.Setup(r => r.FindActiveAsync(_userId, AlertType.SyncFailure, _accountId, default))
            .ReturnsAsync(existing);

        await _service.ResolveSyncFailureAlertAsync(_userId, "truelayer", _accountId);

        _repo.Verify(r => r.ResolveAsync(existing.Id, default), Times.Once);
    }

    [Fact]
    public async Task GenerateSyncFailure_RecentDismissed_SkipsCreation()
    {
        _repo.Setup(r => r.FindActiveAsync(_userId, AlertType.SyncFailure, _accountId, default))
            .ReturnsAsync((Alert?)null);
        _repo.Setup(r => r.HasRecentAsync(
                _userId, AlertType.SyncFailure, _accountId, "Chase", It.IsAny<DateTimeOffset>(), default))
            .ReturnsAsync(true);

        await _service.GenerateSyncFailureAlertAsync(_userId, "truelayer", _accountId, "Chase", "ITEM_LOGIN_REQUIRED");

        _repo.Verify(r => r.AddAsync(It.IsAny<Alert>(), default), Times.Never);
    }

    [Fact]
    public async Task GenerateCashShortfall_NoExisting_AddsWarningAlert()
    {
        var shortfallDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(10);

        _repo.Setup(r => r.FindActiveAsync(_userId, AlertType.CashShortfall, _accountId, default))
            .ReturnsAsync((Alert?)null);
        _repo.Setup(r => r.HasRecentAsync(
                _userId, AlertType.CashShortfall, _accountId, "Chase ···1234", It.IsAny<DateTimeOffset>(), default))
            .ReturnsAsync(false);

        await _service.GenerateCashShortfallAlertAsync(
            _userId, _accountId, "Chase ···1234", shortfallDate, 50m, "EUR");

        _repo.Verify(r => r.AddAsync(It.Is<Alert>(a =>
            a.Type == AlertType.CashShortfall &&
            a.Severity == AlertSeverity.Warning &&
            a.UserId == _userId &&
            a.ReferenceId == _accountId &&
            a.Message.Contains(shortfallDate.ToString("yyyy-MM-dd")) &&
            a.Message.Contains("50.00") &&
            a.Message.Contains("EUR")), default), Times.Once);
    }

    [Fact]
    public async Task GenerateCashShortfall_ExistingActive_SkipsCreation()
    {
        _repo.Setup(r => r.FindActiveAsync(_userId, AlertType.CashShortfall, _accountId, default))
            .ReturnsAsync(new Alert { Id = Guid.NewGuid() });

        await _service.GenerateCashShortfallAlertAsync(
            _userId, _accountId, "Chase ···1234",
            DateOnly.FromDateTime(DateTime.UtcNow).AddDays(5), 25m, "EUR");

        _repo.Verify(r => r.AddAsync(It.IsAny<Alert>(), default), Times.Never);
    }

    [Fact]
    public async Task ResolveCashShortfall_ExistingActive_CallsResolve()
    {
        var existing = new Alert { Id = Guid.NewGuid() };
        _repo.Setup(r => r.FindActiveAsync(_userId, AlertType.CashShortfall, _accountId, default))
            .ReturnsAsync(existing);

        await _service.ResolveCashShortfallAlertAsync(_userId, _accountId);

        _repo.Verify(r => r.ResolveAsync(existing.Id, default), Times.Once);
    }

    [Fact]
    public async Task ResolveCashShortfall_NoExisting_DoesNothing()
    {
        _repo.Setup(r => r.FindActiveAsync(_userId, AlertType.CashShortfall, _accountId, default))
            .ReturnsAsync((Alert?)null);

        await _service.ResolveCashShortfallAlertAsync(_userId, _accountId);

        _repo.Verify(r => r.ResolveAsync(It.IsAny<Guid>(), default), Times.Never);
    }

    [Fact]
    public async Task GeneratePerformanceBrief_NoRecent_AddsInfoAlert()
    {
        _repo.Setup(r => r.HasRecentAsync(
                _userId, AlertType.PerformanceBrief, null, "weekly", It.IsAny<DateTimeOffset>(), default))
            .ReturnsAsync(false);

        await _service.GeneratePerformanceBriefAlertAsync(
            _userId, "Weekly brief: Outperform +2.00% vs SPY", "1W: book +3.00% SPY +1.00%");

        _repo.Verify(r => r.AddAsync(It.Is<Alert>(a =>
            a.Type == AlertType.PerformanceBrief &&
            a.Severity == AlertSeverity.Info &&
            a.UserId == _userId &&
            a.ReferenceId == null &&
            a.ReferenceLabel == "weekly" &&
            a.Title.Contains("Outperform")), default), Times.Once);
    }

    [Fact]
    public async Task GeneratePerformanceBrief_WithinSixDaySuppressWindow_SkipsCreation()
    {
        _repo.Setup(r => r.HasRecentAsync(
                _userId, AlertType.PerformanceBrief, null, "weekly", It.IsAny<DateTimeOffset>(), default))
            .ReturnsAsync(true);

        await _service.GeneratePerformanceBriefAlertAsync(
            _userId, "Weekly brief: Outperform +2.00% vs SPY", "1W: book +3.00% SPY +1.00%");

        _repo.Verify(r => r.AddAsync(It.IsAny<Alert>(), default), Times.Never);
    }

    // --- 044 hygiene sentinels -------------------------------------------------------------
    // Every generator runs the same dedup discipline: an open alert on the same reference wins,
    // then the type's silence window. Each sentinel is pinned on both gates because two of them
    // (DuplicateCharge, CategorySpike) derive their reference id rather than carrying a natural one.

    [Fact]
    public async Task GeneratePriceHike_NoExisting_AddsWarningAlert()
    {
        var subscriptionId = Guid.NewGuid();
        AllowAlert(AlertType.PriceHike);

        await _service.GeneratePriceHikeAlertAsync(_userId, subscriptionId, "Netflix", 10m, 12.50m, "EUR");

        _repo.Verify(r => r.AddAsync(It.Is<Alert>(a =>
            a.Type == AlertType.PriceHike &&
            a.Severity == AlertSeverity.Warning &&
            a.UserId == _userId &&
            a.ReferenceId == subscriptionId &&
            a.ReferenceLabel == "Netflix" &&
            a.Message.Contains("+25%")), default), Times.Once);
    }

    [Fact]
    public async Task GeneratePriceHike_ExistingActive_SkipsCreation()
    {
        SuppressByActiveAlert(AlertType.PriceHike);

        await _service.GeneratePriceHikeAlertAsync(_userId, Guid.NewGuid(), "Netflix", 10m, 12.50m, "EUR");

        VerifyNothingAdded();
        VerifyNoSilenceWindowLookup();
    }

    [Fact]
    public async Task GeneratePriceHike_RecentDismissed_SkipsCreation()
    {
        SuppressBySilenceWindow(AlertType.PriceHike);

        await _service.GeneratePriceHikeAlertAsync(_userId, Guid.NewGuid(), "Netflix", 10m, 12.50m, "EUR");

        VerifyNothingAdded();
    }

    [Fact]
    public async Task GenerateDuplicateCharge_NoExisting_AddsWarningAlertNamingTheMerchant()
    {
        AllowAlert(AlertType.DuplicateCharge);

        await _service.GenerateDuplicateChargeAlertAsync(
            _userId, _accountId, "netflix", "PAYPAL*Netflix.com", 9.99m, "EUR", 2);

        _repo.Verify(r => r.AddAsync(It.Is<Alert>(a =>
            a.Type == AlertType.DuplicateCharge &&
            a.Severity == AlertSeverity.Warning &&
            a.UserId == _userId &&
            a.ReferenceLabel == "netflix" &&
            a.Title.Contains("PAYPAL*Netflix.com") &&
            a.Message.Contains("PAYPAL*Netflix.com") &&
            a.Message.Contains("2×")), default), Times.Once);
    }

    /// <summary>
    /// Dedup rides the normalized key, never the statement spelling: the same duplicate group must
    /// land on one reference however the bank spelled it that day, and two different merchants at
    /// the same amount must not collide on one.
    /// </summary>
    [Fact]
    public async Task GenerateDuplicateCharge_ReferenceAndLabelFollowTheKey_NotTheSpelling()
    {
        var written = new List<Alert>();
        AllowAlert(AlertType.DuplicateCharge);
        _repo.Setup(r => r.AddAsync(It.IsAny<Alert>(), default))
            .Callback<Alert, CancellationToken>((a, _) => written.Add(a))
            .Returns(Task.CompletedTask);

        await _service.GenerateDuplicateChargeAlertAsync(
            _userId, _accountId, "netflix", "NETFLIX.COM", 9.99m, "EUR", 2);
        await _service.GenerateDuplicateChargeAlertAsync(
            _userId, _accountId, "netflix", "PAYPAL*Netflix", 9.99m, "EUR", 2);
        await _service.GenerateDuplicateChargeAlertAsync(
            _userId, _accountId, "spotify", "Spotify AB", 9.99m, "EUR", 2);

        Assert.Equal(3, written.Count);
        Assert.Equal(written[0].ReferenceId, written[1].ReferenceId);
        Assert.NotEqual(written[1].ReferenceId, written[2].ReferenceId);
        // The label is what the silence window matches on, so it tracks the key too.
        Assert.Equal(["netflix", "netflix", "spotify"], written.Select(a => a.ReferenceLabel));
    }

    [Fact]
    public async Task GenerateDuplicateCharge_ExistingActive_SkipsCreation()
    {
        SuppressByActiveAlert(AlertType.DuplicateCharge);

        await _service.GenerateDuplicateChargeAlertAsync(
            _userId, _accountId, "netflix", "Netflix", 9.99m, "EUR", 2);

        VerifyNothingAdded();
        VerifyNoSilenceWindowLookup();
    }

    /// <summary>
    /// The backstop is the only guard left once the user dismisses the alert, and it matches on the
    /// stored label — so the lookup has to use the normalized key, not that run's spelling.
    /// </summary>
    [Fact]
    public async Task GenerateDuplicateCharge_RecentDismissed_SkipsCreation()
    {
        _repo.Setup(r => r.FindActiveAsync(_userId, AlertType.DuplicateCharge, It.IsAny<Guid?>(), default))
            .ReturnsAsync((Alert?)null);
        _repo.Setup(r => r.HasRecentAsync(
                _userId, AlertType.DuplicateCharge, It.IsAny<Guid?>(), "netflix",
                It.IsAny<DateTimeOffset>(), default))
            .ReturnsAsync(true);

        await _service.GenerateDuplicateChargeAlertAsync(
            _userId, _accountId, "netflix", "PAYPAL*Netflix", 9.99m, "EUR", 2);

        VerifyNothingAdded();
    }

    [Fact]
    public async Task GenerateCategorySpike_NoExisting_AddsWarningAlert()
    {
        AllowAlert(AlertType.CategorySpike);

        await _service.GenerateCategorySpikeAlertAsync(_userId, "Groceries", 900m, 300m);

        _repo.Verify(r => r.AddAsync(It.Is<Alert>(a =>
            a.Type == AlertType.CategorySpike &&
            a.Severity == AlertSeverity.Warning &&
            a.UserId == _userId &&
            a.ReferenceLabel == "Groceries" &&
            a.Message.Contains("+200%")), default), Times.Once);
    }

    [Fact]
    public async Task GenerateCategorySpike_ExistingActive_SkipsCreation()
    {
        SuppressByActiveAlert(AlertType.CategorySpike);

        await _service.GenerateCategorySpikeAlertAsync(_userId, "Groceries", 900m, 300m);

        VerifyNothingAdded();
        VerifyNoSilenceWindowLookup();
    }

    [Fact]
    public async Task GenerateCategorySpike_RecentDismissed_SkipsCreation()
    {
        SuppressBySilenceWindow(AlertType.CategorySpike);

        await _service.GenerateCategorySpikeAlertAsync(_userId, "Groceries", 900m, 300m);

        VerifyNothingAdded();
    }

    [Fact]
    public async Task GenerateFxSpread_NoExisting_AddsWarningAlertKeyedOnTheDebitLeg()
    {
        var debitTransactionId = Guid.NewGuid();
        AllowAlert(AlertType.FxSpread);

        await _service.GenerateFxSpreadAlertAsync(_userId, debitTransactionId, "EUR", "USD", 0.95m, 1.00m);

        _repo.Verify(r => r.AddAsync(It.Is<Alert>(a =>
            a.Type == AlertType.FxSpread &&
            a.Severity == AlertSeverity.Warning &&
            a.UserId == _userId &&
            a.ReferenceId == debitTransactionId &&
            a.ReferenceLabel == "EUR/USD" &&
            a.Message.Contains("5%")), default), Times.Once);
    }

    [Fact]
    public async Task GenerateFxSpread_ExistingActive_SkipsCreation()
    {
        SuppressByActiveAlert(AlertType.FxSpread);

        await _service.GenerateFxSpreadAlertAsync(_userId, Guid.NewGuid(), "EUR", "USD", 0.95m, 1.00m);

        VerifyNothingAdded();
        VerifyNoSilenceWindowLookup();
    }

    [Fact]
    public async Task GenerateFxSpread_RecentDismissed_SkipsCreation()
    {
        SuppressBySilenceWindow(AlertType.FxSpread);

        await _service.GenerateFxSpreadAlertAsync(_userId, Guid.NewGuid(), "EUR", "USD", 0.95m, 1.00m);

        VerifyNothingAdded();
    }

    [Fact]
    public async Task GenerateEarningsAhead_Earnings_NoExisting_AddsInfoAlert()
    {
        var eventDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(3);
        AllowAlert(AlertType.EarningsAhead);

        await _service.GenerateEarningsAheadAlertAsync(
            _userId, "AAPL", EarningsAheadEventType.Earnings, eventDate, isEstimate: false);

        _repo.Verify(r => r.AddAsync(It.Is<Alert>(a =>
            a.Type == AlertType.EarningsAhead &&
            a.Severity == AlertSeverity.Info &&
            a.UserId == _userId &&
            a.ReferenceLabel == "AAPL" &&
            a.Title.Contains("AAPL") &&
            a.Message.Contains(eventDate.ToString("yyyy-MM-dd"))), default), Times.Once);
    }

    [Fact]
    public async Task GenerateEarningsAhead_ExDividend_NoExisting_AddsInfoAlertWithExDividendWording()
    {
        var eventDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(2);
        AllowAlert(AlertType.EarningsAhead);

        await _service.GenerateEarningsAheadAlertAsync(
            _userId, "KO", EarningsAheadEventType.ExDividend, eventDate, isEstimate: false);

        _repo.Verify(r => r.AddAsync(It.Is<Alert>(a =>
            a.Type == AlertType.EarningsAhead &&
            a.Title.Contains("Ex-dividend") &&
            a.Message.Contains("ex-dividend")), default), Times.Once);
    }

    /// <summary>
    /// Same ticker, same event, same date must resolve to the same reference id so a second run
    /// (the day after) never re-alerts it — the daily job's core dedup guarantee.
    /// </summary>
    [Fact]
    public async Task GenerateEarningsAhead_SameTickerEventAndDate_ProducesStableReferenceId()
    {
        var eventDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(3);
        var written = new List<Alert>();
        AllowAlert(AlertType.EarningsAhead);
        _repo.Setup(r => r.AddAsync(It.IsAny<Alert>(), default))
            .Callback<Alert, CancellationToken>((a, _) => written.Add(a))
            .Returns(Task.CompletedTask);

        await _service.GenerateEarningsAheadAlertAsync(
            _userId, "AAPL", EarningsAheadEventType.Earnings, eventDate, isEstimate: false);
        await _service.GenerateEarningsAheadAlertAsync(
            _userId, "aapl", EarningsAheadEventType.Earnings, eventDate, isEstimate: true);
        await _service.GenerateEarningsAheadAlertAsync(
            _userId, "AAPL", EarningsAheadEventType.ExDividend, eventDate, isEstimate: false);

        Assert.Equal(written[0].ReferenceId, written[1].ReferenceId);
        Assert.NotEqual(written[0].ReferenceId, written[2].ReferenceId);
    }

    [Fact]
    public async Task GenerateEarningsAhead_ExistingActive_SkipsCreation()
    {
        SuppressByActiveAlert(AlertType.EarningsAhead);

        await _service.GenerateEarningsAheadAlertAsync(
            _userId, "AAPL", EarningsAheadEventType.Earnings,
            DateOnly.FromDateTime(DateTime.UtcNow).AddDays(3), isEstimate: false);

        VerifyNothingAdded();
        VerifyNoSilenceWindowLookup();
    }

    [Fact]
    public async Task GenerateEarningsAhead_RecentDismissed_SkipsCreation()
    {
        SuppressBySilenceWindow(AlertType.EarningsAhead);

        await _service.GenerateEarningsAheadAlertAsync(
            _userId, "AAPL", EarningsAheadEventType.Earnings,
            DateOnly.FromDateTime(DateTime.UtcNow).AddDays(3), isEstimate: false);

        VerifyNothingAdded();
    }

    [Fact]
    public async Task GenerateEarningsAhead_UnknownEventType_SkipsSilently()
    {
        await _service.GenerateEarningsAheadAlertAsync(
            _userId, "AAPL", "dividend", DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1), isEstimate: false);

        VerifyNothingAdded();
        VerifyNoSilenceWindowLookup();
    }

    [Fact]
    public async Task GenerateFilingLanded_NoExisting_AddsInfoAlert()
    {
        var filingDate = DateOnly.FromDateTime(DateTime.UtcNow);
        AllowAlert(AlertType.FilingLanded);

        await _service.GenerateFilingLandedAlertAsync(
            _userId, "AAPL", "10-Q", filingDate, "0001-26-000123", "https://www.sec.gov/doc.htm");

        _repo.Verify(r => r.AddAsync(It.Is<Alert>(a =>
            a.Type == AlertType.FilingLanded &&
            a.Severity == AlertSeverity.Info &&
            a.UserId == _userId &&
            a.ReferenceLabel == "AAPL" &&
            a.Title.Contains("10-Q") &&
            a.Title.Contains("AAPL") &&
            a.Message.Contains(filingDate.ToString("yyyy-MM-dd"))), default), Times.Once);
    }

    /// <summary>
    /// Same ticker, same accession number must resolve to the same reference id — the hourly job's
    /// core dedup guarantee, since EDGAR never reuses an accession number.
    /// </summary>
    [Fact]
    public async Task GenerateFilingLanded_SameTickerAndAccession_ProducesStableReferenceId()
    {
        var filingDate = DateOnly.FromDateTime(DateTime.UtcNow);
        var written = new List<Alert>();
        AllowAlert(AlertType.FilingLanded);
        _repo.Setup(r => r.AddAsync(It.IsAny<Alert>(), default))
            .Callback<Alert, CancellationToken>((a, _) => written.Add(a))
            .Returns(Task.CompletedTask);

        await _service.GenerateFilingLandedAlertAsync(
            _userId, "AAPL", "10-Q", filingDate, "0001-26-000123", "https://www.sec.gov/doc.htm");
        await _service.GenerateFilingLandedAlertAsync(
            _userId, "aapl", "10-Q", filingDate, "0001-26-000123", "https://www.sec.gov/doc.htm");
        await _service.GenerateFilingLandedAlertAsync(
            _userId, "AAPL", "8-K", filingDate, "0001-26-000456", "https://www.sec.gov/other.htm");

        Assert.Equal(written[0].ReferenceId, written[1].ReferenceId);
        Assert.NotEqual(written[0].ReferenceId, written[2].ReferenceId);
    }

    [Fact]
    public async Task GenerateFilingLanded_ExistingActive_SkipsCreation()
    {
        SuppressByActiveAlert(AlertType.FilingLanded);

        await _service.GenerateFilingLandedAlertAsync(
            _userId, "AAPL", "10-Q", DateOnly.FromDateTime(DateTime.UtcNow), "0001-26-000123",
            "https://www.sec.gov/doc.htm");

        VerifyNothingAdded();
        VerifyNoSilenceWindowLookup();
    }

    [Fact]
    public async Task GenerateFilingLanded_RecentDismissed_SkipsCreation()
    {
        SuppressBySilenceWindow(AlertType.FilingLanded);

        await _service.GenerateFilingLandedAlertAsync(
            _userId, "AAPL", "10-Q", DateOnly.FromDateTime(DateTime.UtcNow), "0001-26-000123",
            "https://www.sec.gov/doc.htm");

        VerifyNothingAdded();
    }

    [Fact]
    public async Task GenerateNewsCluster_NoExisting_AddsWarningAlert()
    {
        var day = DateOnly.FromDateTime(DateTime.UtcNow);
        AllowAlert(AlertType.NewsCluster);

        await _service.GenerateNewsClusterAlertAsync(_userId, "AAPL", "2 sources within 2h", day);

        _repo.Verify(r => r.AddAsync(It.Is<Alert>(a =>
            a.Type == AlertType.NewsCluster &&
            a.Severity == AlertSeverity.Warning &&
            a.UserId == _userId &&
            a.ReferenceLabel == "AAPL" &&
            a.Title.Contains("AAPL") &&
            a.Message.Contains("2 sources within 2h")), default), Times.Once);
    }

    /// <summary>
    /// Same ticker, same day must resolve to the same reference id — the 30-min job's core dedup
    /// guarantee, since the job re-checks the same ticker every run within the day.
    /// </summary>
    [Fact]
    public async Task GenerateNewsCluster_SameTickerAndDay_ProducesStableReferenceId()
    {
        var day = DateOnly.FromDateTime(DateTime.UtcNow);
        var written = new List<Alert>();
        AllowAlert(AlertType.NewsCluster);
        _repo.Setup(r => r.AddAsync(It.IsAny<Alert>(), default))
            .Callback<Alert, CancellationToken>((a, _) => written.Add(a))
            .Returns(Task.CompletedTask);

        await _service.GenerateNewsClusterAlertAsync(_userId, "AAPL", "2 sources within 2h", day);
        await _service.GenerateNewsClusterAlertAsync(_userId, "aapl", "thesis-attached source hit", day);
        await _service.GenerateNewsClusterAlertAsync(_userId, "AAPL", "2 sources within 2h", day.AddDays(1));

        Assert.Equal(written[0].ReferenceId, written[1].ReferenceId);
        Assert.NotEqual(written[0].ReferenceId, written[2].ReferenceId);
    }

    [Fact]
    public async Task GenerateNewsCluster_ExistingActive_SkipsCreation()
    {
        SuppressByActiveAlert(AlertType.NewsCluster);

        await _service.GenerateNewsClusterAlertAsync(
            _userId, "AAPL", "2 sources within 2h", DateOnly.FromDateTime(DateTime.UtcNow));

        VerifyNothingAdded();
        VerifyNoSilenceWindowLookup();
    }

    [Fact]
    public async Task GenerateNewsCluster_RecentDismissed_SkipsCreation()
    {
        SuppressBySilenceWindow(AlertType.NewsCluster);

        await _service.GenerateNewsClusterAlertAsync(
            _userId, "AAPL", "2 sources within 2h", DateOnly.FromDateTime(DateTime.UtcNow));

        VerifyNothingAdded();
    }

    [Fact]
    public async Task GenerateBudgetNearLimit_NoExisting_AddsWarningAlert()
    {
        var budgetId = Guid.NewGuid();
        AllowAlert(AlertType.BudgetBreach);

        await _service.GenerateBudgetNearLimitAlertAsync(
            _userId, budgetId, "Groceries", 92m, 100m, 2026, 9);

        _repo.Verify(r => r.AddAsync(It.Is<Alert>(a =>
            a.Type == AlertType.BudgetBreach &&
            a.Severity == AlertSeverity.Warning &&
            a.UserId == _userId &&
            a.ReferenceLabel == "Groceries" &&
            a.Title.Contains("Groceries") &&
            a.Title.Contains("September 2026") &&
            a.Message.Contains("92%") &&
            a.Message.Contains("September 2026")), default), Times.Once);
    }

    [Fact]
    public async Task GenerateBudgetExceeded_NoExisting_AddsWarningAlert()
    {
        var budgetId = Guid.NewGuid();
        AllowAlert(AlertType.BudgetBreach);

        await _service.GenerateBudgetExceededAlertAsync(
            _userId, budgetId, "Groceries", 110m, 100m, 2026, 9);

        _repo.Verify(r => r.AddAsync(It.Is<Alert>(a =>
            a.Type == AlertType.BudgetBreach &&
            a.Severity == AlertSeverity.Warning &&
            a.UserId == _userId &&
            a.ReferenceLabel == "Groceries" &&
            a.Title.Contains("exceeded") &&
            a.Title.Contains("September 2026") &&
            a.Message.Contains("110%") &&
            a.Message.Contains("September 2026")), default), Times.Once);
    }

    [Fact]
    public async Task GenerateBudgetNearLimit_AlreadyRaisedForReference_SkipsCreation()
    {
        _repo.Setup(r => r.ExistsAsync(_userId, AlertType.BudgetBreach, It.IsAny<Guid?>(), default))
            .ReturnsAsync(true);

        await _service.GenerateBudgetNearLimitAlertAsync(
            _userId, Guid.NewGuid(), "Groceries", 92m, 100m, 2026, 9);

        VerifyNothingAdded();
        VerifyNoSilenceWindowLookup();
        _repo.Verify(r => r.FindActiveAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), default), Times.Never);
    }

    /// <summary>
    /// A dismissal sticks for the whole month: the dedup asks whether the reference was EVER raised,
    /// not whether it is still open or was raised recently — so an alert dismissed weeks ago (well
    /// past any silence window) is still not raised again for the same budget and month.
    /// </summary>
    [Fact]
    public async Task GenerateBudgetNearLimit_DismissedEarlierSameMonth_NotRaisedAgain()
    {
        var budgetId = Guid.NewGuid();
        var ledger = TrackAlerts();

        await _service.GenerateBudgetNearLimitAlertAsync(
            _userId, budgetId, "Groceries", 92m, 100m, 2026, 9);
        ledger[0].IsDismissed = true;
        ledger[0].CreatedAt = DateTimeOffset.UtcNow.AddDays(-20);

        await _service.GenerateBudgetNearLimitAlertAsync(
            _userId, budgetId, "Groceries", 97m, 100m, 2026, 9);

        Assert.Single(ledger);
    }

    [Fact]
    public async Task GenerateBudgetBreach_NearLimitThenExceededSameMonth_BothFireOnceEach()
    {
        var budgetId = Guid.NewGuid();
        var ledger = TrackAlerts();

        await _service.GenerateBudgetNearLimitAlertAsync(
            _userId, budgetId, "Groceries", 92m, 100m, 2026, 9);
        ledger[0].IsDismissed = true;
        await _service.GenerateBudgetNearLimitAlertAsync(
            _userId, budgetId, "Groceries", 104m, 100m, 2026, 9);
        await _service.GenerateBudgetExceededAlertAsync(
            _userId, budgetId, "Groceries", 104m, 100m, 2026, 9);
        await _service.GenerateBudgetExceededAlertAsync(
            _userId, budgetId, "Groceries", 110m, 100m, 2026, 9);

        Assert.Equal(2, ledger.Count);
        Assert.Contains("nearing", ledger[0].Title);
        Assert.Contains("exceeded", ledger[1].Title);
    }

    /// <summary>
    /// 90% and 100% are distinct references, so both can be active for the same budget in the same
    /// month — reaching 100% later must still get through even though 90% already alerted.
    /// </summary>
    [Fact]
    public async Task GenerateBudgetBreach_NearLimitAndExceeded_ProduceDifferentReferenceIds()
    {
        var budgetId = Guid.NewGuid();
        var written = new List<Alert>();
        AllowAlert(AlertType.BudgetBreach);
        _repo.Setup(r => r.AddAsync(It.IsAny<Alert>(), default))
            .Callback<Alert, CancellationToken>((a, _) => written.Add(a))
            .Returns(Task.CompletedTask);

        await _service.GenerateBudgetNearLimitAlertAsync(
            _userId, budgetId, "Groceries", 92m, 100m, 2026, 9);
        await _service.GenerateBudgetExceededAlertAsync(
            _userId, budgetId, "Groceries", 105m, 100m, 2026, 9);

        Assert.NotEqual(written[0].ReferenceId, written[1].ReferenceId);
    }

    /// <summary>
    /// Same budget, same crossing kind, same month must resolve to the same reference id — the daily
    /// hygiene run's core "once per budget per month" guarantee. A mid-month limit edit or a refund
    /// that drops spend and later lets it climb back over the line never changes this reference, so
    /// the active-alert dedup above keeps suppressing a second alert for the rest of the month. A new
    /// month is a different reference, so next month can alert again.
    /// </summary>
    [Fact]
    public async Task GenerateBudgetNearLimit_SameBudgetAndMonth_ProducesStableReferenceId()
    {
        var budgetId = Guid.NewGuid();
        var written = new List<Alert>();
        AllowAlert(AlertType.BudgetBreach);
        _repo.Setup(r => r.AddAsync(It.IsAny<Alert>(), default))
            .Callback<Alert, CancellationToken>((a, _) => written.Add(a))
            .Returns(Task.CompletedTask);

        // Same crossing, re-checked the next day with a different spend/limit split (e.g. after a
        // mid-month limit edit, or spend that dipped from a refund and climbed back up) — still the
        // same reference for the month.
        await _service.GenerateBudgetNearLimitAlertAsync(
            _userId, budgetId, "Groceries", 92m, 100m, 2026, 9);
        await _service.GenerateBudgetNearLimitAlertAsync(
            _userId, budgetId, "Groceries", 190m, 200m, 2026, 9);
        // Next month: a fresh reference.
        await _service.GenerateBudgetNearLimitAlertAsync(
            _userId, budgetId, "Groceries", 92m, 100m, 2026, 10);

        Assert.Equal(written[0].ReferenceId, written[1].ReferenceId);
        Assert.NotEqual(written[0].ReferenceId, written[2].ReferenceId);
    }

    [Fact]
    public async Task GenerateMarketStructure_NoExisting_AddsWarningAlert()
    {
        var referenceId = Guid.NewGuid();
        AllowAlert(AlertType.MarketStructure);

        await _service.GenerateMarketStructureAlertAsync(
            _userId, referenceId, "AAPL", "moved 6.2% intraday, above the holding 5% bar");

        _repo.Verify(r => r.AddAsync(It.Is<Alert>(a =>
            a.Type == AlertType.MarketStructure &&
            a.Severity == AlertSeverity.Warning &&
            a.UserId == _userId &&
            a.ReferenceId == referenceId &&
            a.ReferenceLabel == "AAPL" &&
            a.Title.Contains("AAPL") &&
            a.Message.Contains("5% bar")), default), Times.Once);
    }

    /// <summary>
    /// P1 (ledger-heartbeat design): the intraday-move-sentinel job re-checks every held/watchlisted
    /// ticker every 15 minutes, so a name that keeps moving must announce itself once per 24h, not
    /// once per tick — proven here at the generator level (the job always asks with the same
    /// per-ticker reference id) rather than merely asserted in the job's own tests.
    /// </summary>
    [Fact]
    public async Task GenerateMarketStructure_SameTickerWithinTwentyFourHours_SecondCallIsSuppressed()
    {
        var referenceId = Guid.NewGuid();
        var written = new List<Alert>();
        _repo.SetupSequence(r => r.FindActiveAsync(_userId, AlertType.MarketStructure, referenceId, default))
            .ReturnsAsync((Alert?)null)
            .ReturnsAsync(new Alert { Id = Guid.NewGuid() });
        _repo.Setup(r => r.AddAsync(It.IsAny<Alert>(), default))
            .Callback<Alert, CancellationToken>((a, _) => written.Add(a))
            .Returns(Task.CompletedTask);

        await _service.GenerateMarketStructureAlertAsync(_userId, referenceId, "AAPL", "moved 6.2% intraday");
        await _service.GenerateMarketStructureAlertAsync(_userId, referenceId, "AAPL", "moved 6.9% intraday");

        written.Should().ContainSingle("the second tick's active-alert check finds the first alert still open");
    }

    [Fact]
    public async Task GenerateMarketStructure_ExistingActive_SkipsCreation()
    {
        SuppressByActiveAlert(AlertType.MarketStructure);

        await _service.GenerateMarketStructureAlertAsync(
            _userId, Guid.NewGuid(), "AAPL", "moved 6.2% intraday");

        VerifyNothingAdded();
        VerifyNoSilenceWindowLookup();
    }

    /// <summary>
    /// Backstop path: the prior alert was dismissed (no longer active), but it is still inside the
    /// declared 24h window — a re-run must stay quiet rather than re-alert the same ticker.
    /// </summary>
    [Fact]
    public async Task GenerateMarketStructure_DismissedWithinTwentyFourHourWindow_SkipsCreation()
    {
        SuppressBySilenceWindow(AlertType.MarketStructure);

        await _service.GenerateMarketStructureAlertAsync(
            _userId, Guid.NewGuid(), "AAPL", "moved 6.2% intraday");

        VerifyNothingAdded();
    }

    [Fact]
    public async Task GenerateMarketStructure_DifferentTicker_ProducesADifferentReferenceId_AndStillFires()
    {
        var aaplRef = Guid.NewGuid();
        var msftRef = Guid.NewGuid();
        AllowAlert(AlertType.MarketStructure);

        await _service.GenerateMarketStructureAlertAsync(_userId, aaplRef, "AAPL", "moved 6.2% intraday");
        await _service.GenerateMarketStructureAlertAsync(_userId, msftRef, "MSFT", "moved 9.1% intraday");

        _repo.Verify(r => r.AddAsync(It.IsAny<Alert>(), default), Times.Exactly(2));
    }

    /// <summary>
    /// The silence window is looked up by alert type, so a type that reaches the generator without a
    /// declared window throws at alert time — in a background job, where nobody is watching. Reflection
    /// is the price of catching that here instead: the table is an implementation detail with no
    /// public surface worth inventing.
    /// </summary>
    [Fact]
    public void EveryLiveAlertType_DeclaresASilenceWindow()
    {
        // Retired types keep their constant so stored alerts still resolve, but nothing generates
        // them any more — they need no window.
        var retired = new[] { AlertType.UnusualSpend };
        // Deduped once per reference, ever — no silence window applies.
        var oncePerReference = new[] { AlertType.BudgetBreach };

        var declared = (Dictionary<string, TimeSpan>)typeof(AlertGeneratorService)
            .GetField("SilenceWindows", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null)!;

        var live = typeof(AlertType)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .Where(t => !retired.Contains(t) && !oncePerReference.Contains(t))
            .ToList();

        Assert.NotEmpty(live);
        Assert.DoesNotContain(live, t => !declared.ContainsKey(t));
    }

    private void AllowAlert(string type)
    {
        _repo.Setup(r => r.FindActiveAsync(_userId, type, It.IsAny<Guid?>(), default))
            .ReturnsAsync((Alert?)null);
        _repo.Setup(r => r.HasRecentAsync(
                _userId, type, It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<DateTimeOffset>(), default))
            .ReturnsAsync(false);
    }

    private List<Alert> TrackAlerts()
    {
        var ledger = new List<Alert>();
        _repo.Setup(r => r.ExistsAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), default))
            .ReturnsAsync((Guid userId, string type, Guid? referenceId, CancellationToken _) =>
                ledger.Any(a => a.UserId == userId && a.Type == type && a.ReferenceId == referenceId));
        _repo.Setup(r => r.AddAsync(It.IsAny<Alert>(), default))
            .Callback<Alert, CancellationToken>((a, _) => ledger.Add(a))
            .Returns(Task.CompletedTask);
        return ledger;
    }

    private void SuppressByActiveAlert(string type)
        => _repo.Setup(r => r.FindActiveAsync(_userId, type, It.IsAny<Guid?>(), default))
            .ReturnsAsync(new Alert { Id = Guid.NewGuid() });

    private void SuppressBySilenceWindow(string type)
    {
        _repo.Setup(r => r.FindActiveAsync(_userId, type, It.IsAny<Guid?>(), default))
            .ReturnsAsync((Alert?)null);
        _repo.Setup(r => r.HasRecentAsync(
                _userId, type, It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<DateTimeOffset>(), default))
            .ReturnsAsync(true);
    }

    private void VerifyNothingAdded()
        => _repo.Verify(r => r.AddAsync(It.IsAny<Alert>(), default), Times.Never);

    private void VerifyNoSilenceWindowLookup()
        => _repo.Verify(
            r => r.HasRecentAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<string?>(),
                It.IsAny<DateTimeOffset>(), default),
            Times.Never);
}
