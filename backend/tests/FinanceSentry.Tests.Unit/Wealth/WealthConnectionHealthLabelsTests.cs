namespace FinanceSentry.Tests.Unit.Wealth;

using FinanceSentry.Core.Connections;
using FinanceSentry.Core.Headlines;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Wealth.Application.Queries;
using FinanceSentry.Modules.Wealth.Application.Services;
using FinanceSentry.Tests.Unit.Connections;
using FluentAssertions;
using Moq;
using Xunit;

/// <summary>
/// The Accounts page's sync labels from connection health (slice S5): a provider in
/// <see cref="ConnectionHealthOptions.ActiveProviders"/> shows Retrying / Reconnect needed / Dormant from the health its
/// module stores; a shadow provider - and every provider while the switch is empty - shows exactly what it showed before.
/// </summary>
public class WealthConnectionHealthLabelsTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid Token = Guid.NewGuid();

    private readonly Mock<IConnectionHealthReader> _health = new();

    private static BankingAccountSummary Monobank(Guid id, string status = "synced", string last4 = "1111") =>
        new(id, "Monobank", "checking", last4, "monobank", "UAH", 1_000m, 25m, status,
            DateTime.UtcNow.AddMinutes(-10), DateTime.UtcNow.AddMinutes(-10), MonobankCredentialId: Token);

    private static ConnectionHealthEntry Connection(string provider, ConnectionHealthState state, Guid? id = null) =>
        new(provider, "Credential", id ?? Guid.NewGuid(), new ConnectionHealth { State = state });

    private static ConnectionHealthEntry Account(Guid id, ConnectionHealthState state) =>
        new("monobank", "BankAccount", id, new ConnectionHealth { State = state }, IsAccount: true, ConnectionId: Token);

    private Task<WealthSummaryResponse> SummaryAsync(
        IReadOnlyList<BankingAccountSummary> accounts,
        IReadOnlyList<ConnectionHealthEntry> health,
        ConnectionHealthOptions options,
        IReadOnlyList<CryptoHoldingSummary>? crypto = null,
        IReadOnlyList<BrokerageHoldingSummary>? brokerage = null)
    {
        var banking = new Mock<IBankingAccountsReader>();
        banking.Setup(r => r.GetAccountSummariesAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(accounts);
        var cryptoReader = new Mock<ICryptoHoldingsReader>();
        cryptoReader.Setup(r => r.GetHoldingsAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(crypto ?? []);
        var brokerageReader = new Mock<IBrokerageHoldingsReader>();
        brokerageReader.Setup(r => r.GetHoldingsAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(brokerage ?? []);
        _health.Setup(r => r.GetForUserAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(health);

        return new WealthAggregationService(
                banking.Object, Mock.Of<IBankingTransactionReader>(), cryptoReader.Object, brokerageReader.Object,
                null, [_health.Object], HealthOptions.Monitor(options))
            .GetWealthSummaryAsync(UserId, null, null);
    }

    private static InstitutionDto Institution(WealthSummaryResponse summary, string provider) =>
        summary.Categories.SelectMany(c => c.Institutions).Single(i => i.Provider == provider);

    [Fact]
    public async Task With_the_switch_empty_nothing_changes_and_no_health_is_read()
    {
        var account = Guid.NewGuid();

        var summary = await SummaryAsync(
            [Monobank(account, "failed")],
            [Connection("monobank", ConnectionHealthState.ActionRequired, Token), Account(account, ConnectionHealthState.Dormant)],
            HealthOptions.With());

        Institution(summary, "monobank").SyncStatus.Should().Be("failed");
        Institution(summary, "monobank").Accounts.Single().SyncStatus.Should().Be("failed");
        _health.Verify(r => r.GetForUserAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("failed")]
    [InlineData("reauth_required")]
    [InlineData("synced")]
    public async Task A_shadow_provider_keeps_its_own_status_while_another_is_policy_driven(string status)
    {
        var account = Guid.NewGuid();

        var summary = await SummaryAsync(
            [Monobank(account, status)],
            [Connection("monobank", ConnectionHealthState.Failing, Token), Account(account, ConnectionHealthState.Dormant)],
            HealthOptions.With("ibkr"));

        Institution(summary, "monobank").SyncStatus.Should().Be(status);
        Institution(summary, "monobank").Accounts.Single().SyncStatus.Should().Be(status);
    }

    [Theory]
    [InlineData(ConnectionHealthState.Failing, "retrying")]
    [InlineData(ConnectionHealthState.ActionRequired, "reconnect_needed")]
    [InlineData(ConnectionHealthState.Dormant, "dormant")]
    public async Task A_listed_bank_shows_its_connections_health_on_every_account(ConnectionHealthState state, string label)
    {
        Guid[] accounts = [Guid.NewGuid(), Guid.NewGuid()];

        var summary = await SummaryAsync(
            [Monobank(accounts[0], "failed"), Monobank(accounts[1], "failed", "2222")],
            [Connection("monobank", state, Token), Account(accounts[0], state), Account(accounts[1], state)],
            HealthOptions.With("monobank"));

        Institution(summary, "monobank").SyncStatus.Should().Be(label);
        Institution(summary, "monobank").Accounts.Should().OnlyContain(a => a.SyncStatus == label);
    }

    [Fact]
    public async Task A_Degraded_connection_is_silent_and_reads_as_a_Healthy_one_does()
    {
        var account = Guid.NewGuid();

        var summary = await SummaryAsync(
            [Monobank(account, "failed")],
            [Connection("monobank", ConnectionHealthState.Degraded, Token), Account(account, ConnectionHealthState.Degraded)],
            HealthOptions.With("monobank"));

        Institution(summary, "monobank").SyncStatus.Should().Be("synced");
        Institution(summary, "monobank").Accounts.Single().SyncStatus.Should().Be("synced");
        summary.Headline!.Reasons.Should().NotContain(r => r.Code == HeadlineReasonCode.StaleSource && r.Source == "monobank");
    }

    [Fact]
    public async Task A_Degraded_connection_does_not_hide_an_accounts_own_Dormant_label()
    {
        var closed = Guid.NewGuid();
        var working = Guid.NewGuid();

        var summary = await SummaryAsync(
            [Monobank(closed, "failed"), Monobank(working, "synced", "2222")],
            [
                Connection("monobank", ConnectionHealthState.Degraded, Token),
                Account(closed, ConnectionHealthState.Dormant),
                Account(working, ConnectionHealthState.Healthy),
            ],
            HealthOptions.With("monobank"));

        var monobank = Institution(summary, "monobank");
        monobank.Accounts.Single(a => a.AccountId == closed).SyncStatus.Should().Be("dormant");
        monobank.Accounts.Single(a => a.AccountId == working).SyncStatus.Should().Be("synced");
    }

    [Fact]
    public async Task A_Failing_member_still_wins_over_a_Degraded_one()
    {
        BrokerageHoldingSummary[] holdings = [new("AAPL", "STK", 1m, 100m, DateTime.UtcNow, "ibkr")];

        var summary = await SummaryAsync(
            [],
            [Connection("ibkr", ConnectionHealthState.Degraded), Connection("ibkr-flex", ConnectionHealthState.Failing)],
            HealthOptions.With("ibkr", "ibkr-flex"),
            brokerage: holdings);

        Institution(summary, "ibkr").SyncStatus.Should().Be("retrying");
    }

    [Fact]
    public async Task A_Dormant_account_is_labelled_quietly_and_leaves_its_working_siblings_synced()
    {
        var closed = Guid.NewGuid();
        var working = Guid.NewGuid();

        var summary = await SummaryAsync(
            [Monobank(closed, "failed"), Monobank(working, "synced", "2222")],
            [
                Connection("monobank", ConnectionHealthState.Healthy, Token),
                Account(closed, ConnectionHealthState.Dormant),
                Account(working, ConnectionHealthState.Healthy),
            ],
            HealthOptions.With("monobank"));

        var monobank = Institution(summary, "monobank");
        monobank.SyncStatus.Should().Be("synced");
        monobank.Accounts.Single(a => a.AccountId == closed).SyncStatus.Should().Be("dormant");
        monobank.Accounts.Single(a => a.AccountId == working).SyncStatus.Should().Be("synced");
        summary.Headline!.Reasons.Should().Contain(
            r => r.Code == HeadlineReasonCode.StaleSource && r.Source == "monobank",
            "the Dormant account's balance is still frozen behind the total");
    }

    [Fact]
    public async Task A_failure_the_policy_does_not_count_is_not_shown_for_a_listed_bank()
    {
        var account = Guid.NewGuid();

        var summary = await SummaryAsync(
            [Monobank(account, "failed")],
            [Connection("monobank", ConnectionHealthState.Healthy, Token), Account(account, ConnectionHealthState.Healthy)],
            HealthOptions.With("monobank"));

        Institution(summary, "monobank").SyncStatus.Should().Be("synced");
    }

    [Theory]
    [InlineData(ConnectionHealthState.ActionRequired, "reconnect_needed")]
    [InlineData(ConnectionHealthState.Failing, "retrying")]
    [InlineData(ConnectionHealthState.Dormant, "dormant")]
    public async Task A_listed_broker_shows_its_credentials_health(ConnectionHealthState state, string label)
    {
        var summary = await SummaryAsync(
            [], [Connection("ibkr", state)], HealthOptions.With("ibkr"),
            brokerage: [new BrokerageHoldingSummary("AAPL", "STK", 1m, 100m, DateTime.UtcNow, "ibkr")]);

        Institution(summary, "ibkr").SyncStatus.Should().Be(label);
    }

    [Fact]
    public async Task IBKR_Flex_counts_toward_the_IBKR_institution_only_while_it_is_listed_itself()
    {
        BrokerageHoldingSummary[] holdings = [new("AAPL", "STK", 1m, 100m, DateTime.UtcNow, "ibkr")];
        ConnectionHealthEntry[] health =
            [Connection("ibkr", ConnectionHealthState.Healthy), Connection("ibkr-flex", ConnectionHealthState.Failing)];

        var shadowFlex = await SummaryAsync([], health, HealthOptions.With("ibkr"), brokerage: holdings);
        var listedFlex = await SummaryAsync([], health, HealthOptions.With("ibkr", "ibkr-flex"), brokerage: holdings);

        Institution(shadowFlex, "ibkr").SyncStatus.Should().Be("synced");
        Institution(listedFlex, "ibkr").SyncStatus.Should().Be("retrying");
    }

    [Fact]
    public async Task A_listed_exchange_shows_its_credentials_health_and_a_shadow_one_stays_synced()
    {
        CryptoHoldingSummary[] holdings =
        [
            new("BTC", 1m, 0m, 50_000m, DateTime.UtcNow, "binance"),
            new("ETH", 1m, 0m, 2_000m, DateTime.UtcNow, "revolut_x"),
        ];

        var summary = await SummaryAsync(
            [],
            [Connection("binance", ConnectionHealthState.Dormant), Connection("revolut_x", ConnectionHealthState.ActionRequired)],
            HealthOptions.With("binance"),
            crypto: holdings);

        Institution(summary, "binance").SyncStatus.Should().Be("dormant");
        Institution(summary, "revolut_x").SyncStatus.Should().Be("synced");
    }

    [Fact]
    public async Task A_health_read_that_fails_leaves_every_status_as_its_path_gives_it()
    {
        var account = Guid.NewGuid();
        _health.Setup(r => r.GetForUserAsync(UserId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db down"));
        var banking = new Mock<IBankingAccountsReader>();
        banking.Setup(r => r.GetAccountSummariesAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([Monobank(account, "failed")]);

        var summary = await new WealthAggregationService(
                banking.Object, Mock.Of<IBankingTransactionReader>(), null, null, null,
                [_health.Object], HealthOptions.Monitor(HealthOptions.With("monobank")))
            .GetWealthSummaryAsync(UserId, null, null);

        Institution(summary, "monobank").SyncStatus.Should().Be("failed");
    }
}
