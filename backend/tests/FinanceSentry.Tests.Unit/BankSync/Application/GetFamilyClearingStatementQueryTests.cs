namespace FinanceSentry.Tests.Unit.BankSync.Application;

using FinanceSentry.Modules.BankSync.Application.Queries;
using FinanceSentry.Modules.BankSync.Application.Services;
using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Domain.Repositories;
using FluentAssertions;
using Moq;
using Xunit;

/// <summary>
/// Unit tests for GetFamilyClearingStatementQueryHandler (issue #434, Ship 2). The handler reads
/// ICounterpartyClassificationService only, so every test hands it a hand-built
/// CounterpartyClassificationResult rather than transactions — no second classification path.
/// </summary>
public class GetFamilyClearingStatementQueryTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private const int Months = 6;
    private const string Month = "2026-05";

    private static Mock<ICounterpartyClassificationService> ClassificationReturning(
        params CounterpartyMonthlyFlow[] flows)
    {
        var mock = new Mock<ICounterpartyClassificationService>();
        mock.Setup(s => s.ClassifyForWindowAsync(UserId, Months, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CounterpartyClassificationResult([], flows));
        return mock;
    }

    private static async Task<FamilyClearingStatement> RunAsync(
        Mock<ICounterpartyClassificationService> classification, string? month = Month,
        IReadOnlyList<Counterparty>? counterparties = null)
    {
        var sut = new GetFamilyClearingStatementQueryHandler(
            classification.Object, CounterpartyRepoWith(counterparties ?? []));
        return await sut.Handle(new GetFamilyClearingStatementQuery(UserId, month, Months), CancellationToken.None);
    }

    private static ICounterpartyRepository CounterpartyRepoWith(IReadOnlyList<Counterparty> counterparties)
    {
        var mock = new Mock<ICounterpartyRepository>();
        mock.Setup(r => r.GetForUserAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(counterparties);
        return mock.Object;
    }

    private static Counterparty MakeCounterparty(
        string name, string flowRole, decimal? expectedAmount = null, string? expectedCurrency = null)
        => new()
        {
            UserId = UserId,
            Name = name,
            FlowRole = flowRole,
            ExpectedMonthlyInflowAmount = expectedAmount,
            ExpectedMonthlyInflowCurrency = expectedCurrency,
        };

    // ── done_when #1: gross received/sent, both directions in full ─────────────

    [Fact]
    public async Task Handle_CounterpartyWithBothDirections_ReportsGrossReceivedAndSentInFull()
    {
        var classification = ClassificationReturning(
            new CounterpartyMonthlyFlow(Month, "Mom", FlowRoles.FamilySupport, 720m, 500m));

        var statement = await RunAsync(classification);

        statement.Counterparties.Should().ContainSingle();
        var line = statement.Counterparties[0];
        line.Name.Should().Be("Mom");
        line.FlowRole.Should().Be(FlowRoles.FamilySupport);
        line.ReceivedUsd.Should().Be(720m);
        line.SentUsd.Should().Be(500m);
    }

    // ── done_when #2: native per-currency subtotals, two currencies → two entries ──

    [Fact]
    public async Task Handle_CounterpartyWithTwoCurrencies_ProducesTwoByCurrencyEntries()
    {
        var flow = new CounterpartyMonthlyFlow(
            Month, "Mom", FlowRoles.FamilySupport, 720m, 500m,
            ByCurrency:
            [
                new CounterpartyCurrencyFlow("EUR", 200m, 0m),
                new CounterpartyCurrencyFlow("UAH", 20000m, 18500m),
            ]);
        var classification = ClassificationReturning(flow);

        var statement = await RunAsync(classification);

        var line = statement.Counterparties.Single();
        line.ByCurrency.Should().HaveCount(2);
        line.ByCurrency.Select(c => c.Currency).Should().Equal("EUR", "UAH"); // ordinal-ordered
        var eur = line.ByCurrency.Single(c => c.Currency == "EUR");
        eur.Received.Should().Be(200m);
        eur.Sent.Should().Be(0m);
        var uah = line.ByCurrency.Single(c => c.Currency == "UAH");
        uah.Received.Should().Be(20000m);
        uah.Sent.Should().Be(18500m);
    }

    // ── done_when #3: NetUsd is presentational only ─────────────────────────────

    [Fact]
    public async Task Handle_NetUsd_IsReceivedMinusSent_PresentationalOnly()
    {
        var classification = ClassificationReturning(
            new CounterpartyMonthlyFlow(Month, "Mom", FlowRoles.FamilySupport, 720m, 500m));

        var statement = await RunAsync(classification);

        statement.Counterparties.Single().NetUsd.Should().Be(220m);
    }

    // ── done_when #4: the reconciliation invariant ──────────────────────────────

    [Fact]
    public async Task Handle_SupportTotalUsd_EqualsMonthlyFlowFamilySupportOutflowUsd_ForSameMonthAndWindow()
    {
        // One classification result drives both readers, exactly like the dashboard does —
        // so a drift between the statement and the money flow would be a real bug, not a
        // test artifact.
        var (account, accountId) = MakeAccount("USD");
        var flows = new CounterpartyMonthlyFlow[]
        {
            new(Month, "Mom", FlowRoles.FamilySupport, 720m, 500m),
            new(Month, "Aunt Vira", FlowRoles.FamilySupport, 0m, 150m),
            new(Month, "Investment routing", FlowRoles.Investment, 0m, 300m),
        };
        var classification = new CounterpartyClassificationResult([], flows);

        var statementHandler = new GetFamilyClearingStatementQueryHandler(
            ClassificationServiceReturning(classification), CounterpartyRepoWith([]));
        var statement = await statementHandler.Handle(
            new GetFamilyClearingStatementQuery(UserId, Month, Months), CancellationToken.None);

        var moneyFlowService = new MoneyFlowStatisticsService(
            TransactionRepoWithNoTransactions(), AccountRepoWith(account),
            new TransferDetectionService(), CommittedOutflowPolicy());
        var monthlyFlows = await moneyFlowService.GetMonthlyFlowAsync(UserId, classification, Months);
        var monthRow = monthlyFlows.Single(f => f.Month == Month && f.Currency == "USD");

        statement.SupportTotalUsd.Should().Be(monthRow.FamilySupportOutflowUsd);
        statement.SupportTotalUsd.Should().Be(650m); // 500 + 150, investment excluded
        _ = accountId;
    }

    // ── done_when #5: self_routing/investment excluded; ExcludedRoutingLegs counted ──

    [Fact]
    public async Task Handle_ExcludesSelfRoutingAndInvestment_AndCountsSelfRoutingLegs()
    {
        var classification = ClassificationReturning(
            new CounterpartyMonthlyFlow(Month, "Mom", FlowRoles.FamilySupport, 720m, 500m),
            new CounterpartyMonthlyFlow(Month, "Own EUR hop", FlowRoles.SelfRouting, 400m, 400m),
            new CounterpartyMonthlyFlow(Month, "Own UAH hop", FlowRoles.SelfRouting, 100m, 100m),
            new CounterpartyMonthlyFlow(Month, "Brokerage", FlowRoles.Investment, 0m, 300m));

        var statement = await RunAsync(classification);

        statement.Counterparties.Should().ContainSingle(l => l.Name == "Mom");
        statement.Counterparties.Should().NotContain(l => l.FlowRole == FlowRoles.SelfRouting);
        statement.Counterparties.Should().NotContain(l => l.FlowRole == FlowRoles.Investment);
        statement.ExcludedRoutingLegs.Should().Be(2);
    }

    // ── done_when #6: empty month → empty lines, zero totals, not an error ─────

    [Fact]
    public async Task Handle_MonthWithNoCounterpartyActivity_ReturnsEmptyStatement()
    {
        var classification = ClassificationReturning(
            new CounterpartyMonthlyFlow("2026-04", "Mom", FlowRoles.FamilySupport, 720m, 500m));

        var statement = await RunAsync(classification, Month);

        statement.Counterparties.Should().BeEmpty();
        statement.SupportTotalUsd.Should().Be(0m);
        statement.ReceivedTotalUsd.Should().Be(0m);
        statement.ExcludedRoutingLegs.Should().Be(0);
    }

    // ── done_when #7: deterministic ordering on re-run ──────────────────────────

    [Fact]
    public async Task Handle_ReRunOverFixedMonth_ReturnsIdenticalIdenticallyOrderedOutput()
    {
        var classification = ClassificationReturning(
            new CounterpartyMonthlyFlow(Month, "Zoya", FlowRoles.FamilySupport, 100m, 0m),
            new CounterpartyMonthlyFlow(Month, "Aunt Vira", FlowRoles.FamilySupport, 0m, 50m),
            new CounterpartyMonthlyFlow(Month, "Mom", FlowRoles.FamilySupport, 720m, 500m));

        var first = await RunAsync(classification);
        var second = await RunAsync(classification);

        first.Should().BeEquivalentTo(second, o => o.WithStrictOrdering());
        first.Counterparties.Select(l => l.Name).Should().Equal("Aunt Vira", "Mom", "Zoya");
    }

    // ── Ship 3: rent fields (issue #434) ────────────────────────────────────────

    [Fact]
    public async Task Handle_NativeReceivedExactlyMatchesExpected_RentConfirmedTrue_NoShortfall()
    {
        var flow = new CounterpartyMonthlyFlow(
            Month, "Tenant", FlowRoles.FamilySupport, 300m, 0m,
            ByCurrency: [new CounterpartyCurrencyFlow("EUR", 500m, 0m)]);
        var classification = ClassificationReturning(flow);
        var counterparty = MakeCounterparty("Tenant", FlowRoles.FamilySupport, 500m, "EUR");

        var statement = await RunAsync(classification, counterparties: [counterparty]);

        var line = statement.Counterparties.Single();
        line.RentExpectedAmount.Should().Be(500m);
        line.RentExpectedCurrency.Should().Be("EUR");
        line.RentConfirmed.Should().BeTrue();
        line.RentShortfall.Should().BeNull();
    }

    [Fact]
    public async Task Handle_NativeReceivedBelowExpected_RentConfirmedFalse_ShortfallIsGap()
    {
        var flow = new CounterpartyMonthlyFlow(
            Month, "Tenant", FlowRoles.FamilySupport, 240m, 0m,
            ByCurrency: [new CounterpartyCurrencyFlow("EUR", 400m, 0m)]);
        var classification = ClassificationReturning(flow);
        var counterparty = MakeCounterparty("Tenant", FlowRoles.FamilySupport, 500m, "EUR");

        var statement = await RunAsync(classification, counterparties: [counterparty]);

        var line = statement.Counterparties.Single();
        line.RentConfirmed.Should().BeFalse();
        line.RentShortfall.Should().Be(100m);
    }

    [Fact]
    public async Task Handle_NativeReceivedAboveExpected_RentConfirmedTrue_NoShortfall()
    {
        var flow = new CounterpartyMonthlyFlow(
            Month, "Tenant", FlowRoles.FamilySupport, 330m, 0m,
            ByCurrency: [new CounterpartyCurrencyFlow("EUR", 550m, 0m)]);
        var classification = ClassificationReturning(flow);
        var counterparty = MakeCounterparty("Tenant", FlowRoles.FamilySupport, 500m, "EUR");

        var statement = await RunAsync(classification, counterparties: [counterparty]);

        var line = statement.Counterparties.Single();
        line.RentConfirmed.Should().BeTrue();
        line.RentShortfall.Should().BeNull();
    }

    [Fact]
    public async Task Handle_NoExpectationConfigured_AllRentFieldsNull()
    {
        var flow = new CounterpartyMonthlyFlow(
            Month, "Mom", FlowRoles.FamilySupport, 720m, 500m,
            ByCurrency: [new CounterpartyCurrencyFlow("EUR", 720m, 500m)]);
        var classification = ClassificationReturning(flow);
        var counterparty = MakeCounterparty("Mom", FlowRoles.FamilySupport);

        var statement = await RunAsync(classification, counterparties: [counterparty]);

        var line = statement.Counterparties.Single();
        line.RentExpectedAmount.Should().BeNull();
        line.RentExpectedCurrency.Should().BeNull();
        line.RentConfirmed.Should().BeNull();
        line.RentShortfall.Should().BeNull();
    }

    [Fact]
    public async Task Handle_InboundInDifferentCurrency_DoesNotCountTowardConfirmation()
    {
        var flow = new CounterpartyMonthlyFlow(
            Month, "Tenant", FlowRoles.FamilySupport, 600m, 0m,
            ByCurrency: [new CounterpartyCurrencyFlow("UAH", 20000m, 0m)]);
        var classification = ClassificationReturning(flow);
        var counterparty = MakeCounterparty("Tenant", FlowRoles.FamilySupport, 500m, "EUR");

        var statement = await RunAsync(classification, counterparties: [counterparty]);

        var line = statement.Counterparties.Single();
        line.RentConfirmed.Should().BeFalse();
        line.RentShortfall.Should().Be(500m); // 0 native EUR received, full expected amount short
    }

    [Fact]
    public async Task Handle_NonFamilySupportCounterpartyWithExpectationSet_StillGetsNullRentFields()
    {
        // The statement itself only ever projects family_support flows into lines, but this
        // guards the lookup directly: even if a family_support flow shared a name with a
        // non-family_support counterparty row, that row must never leak its expectation in.
        var flow = new CounterpartyMonthlyFlow(
            Month, "Brokerage", FlowRoles.FamilySupport, 300m, 0m,
            ByCurrency: [new CounterpartyCurrencyFlow("USD", 300m, 0m)]);
        var classification = ClassificationReturning(flow);
        var counterparty = MakeCounterparty("Brokerage", FlowRoles.Investment, 500m, "USD");

        var statement = await RunAsync(classification, counterparties: [counterparty]);

        var line = statement.Counterparties.Single();
        line.RentExpectedAmount.Should().BeNull();
        line.RentExpectedCurrency.Should().BeNull();
        line.RentConfirmed.Should().BeNull();
        line.RentShortfall.Should().BeNull();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static ICounterpartyClassificationService ClassificationServiceReturning(
        CounterpartyClassificationResult result)
    {
        var mock = new Mock<ICounterpartyClassificationService>();
        mock.Setup(s => s.ClassifyForWindowAsync(UserId, Months, It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
        return mock.Object;
    }

    private static (BankAccount account, Guid accountId) MakeAccount(string currency)
    {
        var a = new BankAccount(UserId, $"item_{Guid.NewGuid():N}", "Bank", "checking", "1234", "Owner", currency, UserId, "truelayer");
        return (a, a.Id);
    }

    private static ITransactionRepository TransactionRepoWithNoTransactions()
    {
        var mock = new Mock<ITransactionRepository>();
        mock.Setup(r => r.GetByUserIdSinceAsync(UserId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        return mock.Object;
    }

    private static IBankAccountRepository AccountRepoWith(BankAccount account)
    {
        var mock = new Mock<IBankAccountRepository>();
        mock.Setup(r => r.GetByUserIdAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync([account]);
        return mock.Object;
    }

    private static ICommittedOutflowPolicy CommittedOutflowPolicy()
    {
        var mock = new Mock<ICommittedOutflowPolicy>();
        mock.Setup(p => p.LoadForUserAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommittedOutflowRules
            {
                ActiveCommitmentKeys = new HashSet<string>(StringComparer.Ordinal),
                PinnedMerchantKeys = new HashSet<string>(StringComparer.Ordinal),
            });
        return mock.Object;
    }
}
