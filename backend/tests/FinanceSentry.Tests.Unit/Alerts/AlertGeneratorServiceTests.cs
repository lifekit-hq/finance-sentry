namespace FinanceSentry.Tests.Unit.Alerts;

using System.Reflection;
using FinanceSentry.Modules.Alerts.Application.Services;
using FinanceSentry.Modules.Alerts.Domain;
using FinanceSentry.Modules.Alerts.Domain.Repositories;
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

        var declared = (Dictionary<string, TimeSpan>)typeof(AlertGeneratorService)
            .GetField("SilenceWindows", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null)!;

        var live = typeof(AlertType)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .Where(t => !retired.Contains(t))
            .ToList();

        Assert.NotEmpty(live);
        Assert.Empty(live.Where(t => !declared.ContainsKey(t)));
    }

    private void AllowAlert(string type)
    {
        _repo.Setup(r => r.FindActiveAsync(_userId, type, It.IsAny<Guid?>(), default))
            .ReturnsAsync((Alert?)null);
        _repo.Setup(r => r.HasRecentAsync(
                _userId, type, It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<DateTimeOffset>(), default))
            .ReturnsAsync(false);
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
