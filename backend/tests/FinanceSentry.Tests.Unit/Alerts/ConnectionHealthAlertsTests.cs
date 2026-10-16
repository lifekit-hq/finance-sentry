namespace FinanceSentry.Tests.Unit.Alerts;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Alerts.Application.Services;
using FinanceSentry.Modules.Alerts.Domain;
using FinanceSentry.Modules.Alerts.Domain.Repositories;
using Moq;
using Xunit;

/// <summary>
/// The alerts the connection-health policy raises for a provider it drives (slice S5): the reconnect ask links to the
/// provider's reconnect and is marked ActionRequired for Companion, and the one Dormant notice is raised once per
/// connection or account, ever - a dismissed notice stays dismissed.
/// </summary>
public class ConnectionHealthAlertsTests
{
    private readonly Mock<IAlertRepository> _repo = new();
    private readonly AlertGeneratorService _service;
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _accountId = Guid.NewGuid();

    public ConnectionHealthAlertsTests() =>
        _service = new AlertGeneratorService(_repo.Object, Mock.Of<IPolicyAckReader>());

    [Fact]
    public async Task The_reconnect_ask_links_to_the_providers_reconnect_and_is_marked_ActionRequired()
    {
        await _service.EscalateSyncFailureAlertAsync(_userId, "ibkr", null, null, "HTTP_401");

        _repo.Verify(r => r.AddAsync(It.Is<Alert>(a =>
            a.Type == AlertType.SyncFailure
            && a.AppPath == "/accounts/list?reconnect=ibkr"
            && a.HealthState == "ActionRequired"), default), Times.Once);
    }

    [Theory]
    [InlineData(SyncFailureClass.Outage)]
    [InlineData(SyncFailureClass.Unknown)]
    [InlineData(SyncFailureClass.Credential)]
    public async Task Every_other_sync_failure_keeps_the_list_link_and_carries_no_health_state(SyncFailureClass failureClass)
    {
        await _service.GenerateSyncFailureAlertAsync(_userId, "monobank", _accountId, "Monobank", "HTTP_500", failureClass);

        _repo.Verify(r => r.AddAsync(It.Is<Alert>(a =>
            a.AppPath == "/accounts/list" && a.HealthState == null), default), Times.Once);
    }

    [Fact]
    public async Task A_Dormant_account_gets_one_info_notice_that_says_it_may_be_closed()
    {
        await _service.GenerateConnectionDormantAlertAsync(
            _userId, "monobank", _accountId, "Monobank ···1234", TimeSpan.FromDays(7.2), othersSyncing: true);

        _repo.Verify(r => r.AddAsync(It.Is<Alert>(a =>
            a.Type == AlertType.ConnectionDormant
            && a.Severity == AlertSeverity.Info
            && a.ReferenceId == _accountId
            && a.HealthState == "Dormant"
            && a.AppPath == "/accounts/list"
            && a.Title == "Monobank ···1234 may be closed"
            && a.Message.Contains("has not synced for 7 days, while your other accounts with the same bank sync fine")
            && a.Message.Contains("It may be closed.")), default), Times.Once);
    }

    [Fact]
    public async Task A_Dormant_connection_says_so_without_naming_siblings()
    {
        await _service.GenerateConnectionDormantAlertAsync(
            _userId, "ibkr", null, null, TimeSpan.FromDays(8), othersSyncing: false);

        _repo.Verify(r => r.AddAsync(It.Is<Alert>(a =>
            a.Type == AlertType.ConnectionDormant
            && a.Title == "ibkr may be closed"
            && a.Message.StartsWith("ibkr has not synced for 8 days. It may be closed.", StringComparison.Ordinal)), default), Times.Once);
    }

    [Fact]
    public async Task The_Dormant_notice_is_never_raised_twice_for_the_same_account_even_once_dismissed()
    {
        // Any row on the reference - open, dismissed or resolved - holds the next one back.
        _repo.Setup(r => r.ExistsAsync(_userId, AlertType.ConnectionDormant, _accountId, default)).ReturnsAsync(true);

        await _service.GenerateConnectionDormantAlertAsync(
            _userId, "monobank", _accountId, "Monobank ···1234", TimeSpan.FromDays(7), othersSyncing: true);

        _repo.Verify(r => r.AddAsync(It.IsAny<Alert>(), default), Times.Never);
    }

    [Fact]
    public async Task The_Dormant_notice_is_resolved_on_its_own_reference()
    {
        var open = new Alert { Id = Guid.NewGuid(), Type = AlertType.ConnectionDormant, ReferenceId = _accountId };
        _repo.Setup(r => r.FindActiveAsync(_userId, AlertType.ConnectionDormant, _accountId, default)).ReturnsAsync(open);

        await _service.ResolveConnectionDormantAlertAsync(_userId, "monobank", _accountId);

        _repo.Verify(r => r.ResolveAsync(open.Id, default), Times.Once);
    }
}
