namespace FinanceSentry.Tests.Unit.Alerts;

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

        await _service.GenerateSyncFailureAlertAsync(_userId, "plaid", _accountId, "Chase", "ITEM_LOGIN_REQUIRED");

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

        await _service.ResolveSyncFailureAlertAsync(_userId, "plaid", _accountId);

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

        await _service.GenerateSyncFailureAlertAsync(_userId, "plaid", _accountId, "Chase", "ITEM_LOGIN_REQUIRED");

        _repo.Verify(r => r.AddAsync(It.IsAny<Alert>(), default), Times.Never);
    }

    [Fact]
    public async Task GenerateUnusualSpend_NoExisting_AddsInfoAlert()
    {
        _repo.Setup(r => r.FindActiveAsync(_userId, AlertType.UnusualSpend, null, default))
            .ReturnsAsync((Alert?)null);
        _repo.Setup(r => r.HasRecentAsync(
                _userId, AlertType.UnusualSpend, null, "Restaurants", It.IsAny<DateTimeOffset>(), default))
            .ReturnsAsync(false);

        await _service.GenerateUnusualSpendAlertAsync(_userId, "Restaurants", 1200m, 400m);

        _repo.Verify(r => r.AddAsync(It.Is<Alert>(a =>
            a.Type == AlertType.UnusualSpend &&
            a.Severity == AlertSeverity.Info &&
            a.ReferenceLabel == "Restaurants"), default), Times.Once);
    }

    [Fact]
    public async Task GenerateUnusualSpend_RecentDismissed_SkipsCreation()
    {
        _repo.Setup(r => r.FindActiveAsync(_userId, AlertType.UnusualSpend, null, default))
            .ReturnsAsync((Alert?)null);
        _repo.Setup(r => r.HasRecentAsync(
                _userId, AlertType.UnusualSpend, null, "Restaurants", It.IsAny<DateTimeOffset>(), default))
            .ReturnsAsync(true);

        await _service.GenerateUnusualSpendAlertAsync(_userId, "Restaurants", 1200m, 400m);

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
}
