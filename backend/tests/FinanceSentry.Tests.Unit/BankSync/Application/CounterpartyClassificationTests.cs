namespace FinanceSentry.Tests.Unit.BankSync.Application;

using FinanceSentry.Core.Utils;
using FinanceSentry.Modules.BankSync.Application.Services;
using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Domain.Repositories;
using FluentAssertions;
using Moq;
using Xunit;

/// <summary>
/// Unit tests for CounterpartyClassificationService (spec 044, US1 + US2).
/// </summary>
public class CounterpartyClassificationTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid AccountId = Guid.NewGuid();

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static Transaction MakeTx(
        decimal amount,
        string type,
        string description,
        string? merchantName = null,
        DateTime? date = null,
        bool isPending = false,
        Guid? accountId = null)
    {
        var d = date ?? new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc);
        var tx = new Transaction(accountId ?? AccountId, UserId, amount, d, description, Guid.NewGuid().ToString("N"), isPending)
        {
            TransactionType = type,
            MerchantName = merchantName,
            PostedDate = isPending ? null : d,
            IsActive = true
        };
        return tx;
    }

    private static Counterparty MakeCounterparty(string name, params (string matchType, string pattern)[] rules)
        => MakeCounterparty(name, FlowRoles.FamilySupport, rules);

    private static Counterparty MakeCounterparty(
        string name, string flowRole, params (string matchType, string pattern)[] rules)
    {
        var cp = new Counterparty { UserId = Guid.Empty, Name = name, FlowRole = flowRole };
        foreach (var (mt, pat) in rules)
            cp.Rules.Add(new CounterpartyRule { CounterpartyId = cp.Id, MatchType = mt, Pattern = pat });
        return cp;
    }

    private static BankAccount MakeAccount(string currency)
        => new(UserId, $"item_{Guid.NewGuid():N}", "Bank", "checking", "1234", "Owner", currency, UserId, "truelayer");

    private static ICounterpartyClassificationService BuildSut(params Counterparty[] counterparties)
        => BuildSutForWindow([], MakeAccount("USD"), counterparties);

    /// <summary>SUT whose repositories also serve the window path (<c>ClassifyForWindowAsync</c>).</summary>
    private static ICounterpartyClassificationService BuildSutForWindow(
        IReadOnlyList<Transaction> windowTransactions, BankAccount account, params Counterparty[] counterparties)
    {
        var repoMock = new Mock<ICounterpartyRepository>();
        repoMock.Setup(r => r.GetForUserAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(counterparties.ToList());

        var txMock = new Mock<ITransactionRepository>();
        txMock.Setup(r => r.GetByUserIdSinceAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(windowTransactions.ToList());

        var acctMock = new Mock<IBankAccountRepository>();
        acctMock.Setup(r => r.GetByUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([account]);

        return new CounterpartyClassificationService(repoMock.Object, txMock.Object, acctMock.Object);
    }

    private static readonly Dictionary<Guid, string> UahAccount = new() { [AccountId] = "UAH" };
    private static readonly Dictionary<Guid, string> UsdAccount = new() { [AccountId] = "USD" };

    // ── T044-01: Match by description_contains ────────────────────────────────

    [Fact]
    public async Task Classify_DescriptionContains_MatchesCreditTransaction()
    {
        var cp = MakeCounterparty("Людмила Сичова", ("description_contains", "Людмила Сичова"));
        var sut = BuildSut(cp);

        var credit = MakeTx(18000m, "credit", "Від: Людмила Сичова");
        var result = await sut.ClassifyAsync(UserId, [credit], UahAccount);

        result.MatchedTransactionIds.Should().Contain(credit.Id);
        result.MonthlyFlows.Should().HaveCount(1);
        result.MonthlyFlows[0].InflowUsd.Should().BeApproximately(
            CurrencyConverter.ToUsd(18000m, "UAH"), 0.01m);
        result.MonthlyFlows[0].OutflowUsd.Should().Be(0m);
    }

    // ── T044-02: Match by merchant_name_contains ──────────────────────────────

    [Fact]
    public async Task Classify_MerchantNameContains_MatchesDebitTransaction()
    {
        var cp = MakeCounterparty("Ліза", ("merchant_name_contains", "Єлизавета"));
        var sut = BuildSut(cp);

        var debit = MakeTx(5000m, "debit", "card transfer", merchantName: "Єлизавета Морозова");
        var result = await sut.ClassifyAsync(UserId, [debit], UsdAccount);

        result.MatchedTransactionIds.Should().Contain(debit.Id);
        result.MonthlyFlows[0].OutflowUsd.Should().Be(5000m);
        result.MonthlyFlows[0].InflowUsd.Should().Be(0m);
    }

    // ── T044-03: No match returns empty result ────────────────────────────────

    [Fact]
    public async Task Classify_NoMatch_ReturnsEmptyResult()
    {
        var cp = MakeCounterparty("Мама", ("description_contains", "мама"));
        var sut = BuildSut(cp);

        var unrelated = MakeTx(100m, "debit", "Grocery store");
        var result = await sut.ClassifyAsync(UserId, [unrelated], UsdAccount);

        result.MatchedTransactionIds.Should().BeEmpty();
        result.MonthlyFlows.Should().BeEmpty();
    }

    // ── T044-04: Rent in AND support out in the same month — both reported gross ──

    [Fact]
    public async Task Classify_CreditsAndDebitsSameMonth_ReportsBothDirectionsGross()
    {
        // The real white-card month: ₴18k rent arrives, ₴13k of support goes back out.
        // Both are facts. Netting them to ₴5k of income hid the ₴13k of spending, which is
        // the transfer-blind savings rate this feature exists to fix.
        var cp = MakeCounterparty("Мама", ("description_contains", "мама"));
        var sut = BuildSut(cp);

        var month = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
        var credit = MakeTx(18000m, "credit", "від мама", date: month.AddDays(1));
        var debit1 = MakeTx(8000m, "debit", "мама допомога", date: month.AddDays(10));
        var debit2 = MakeTx(5000m, "debit", "мама переказ", date: month.AddDays(20));

        var result = await sut.ClassifyAsync(UserId, [credit, debit1, debit2], UahAccount);

        result.MatchedTransactionIds.Should().BeEquivalentTo([credit.Id, debit1.Id, debit2.Id]);
        var flow = result.MonthlyFlows.Should().ContainSingle().Subject;
        flow.InflowUsd.Should().BeApproximately(CurrencyConverter.ToUsd(18000m, "UAH"), 0.01m);
        flow.OutflowUsd.Should().BeApproximately(CurrencyConverter.ToUsd(13000m, "UAH"), 0.01m);
    }

    // ── T044-05: Debits dominate — the credit is still income ────────────────

    [Fact]
    public async Task Classify_DebitsDominateMonth_SmallerCreditStillCountsAsInflow()
    {
        var cp = MakeCounterparty("Мама", ("description_contains", "мама"));
        var sut = BuildSut(cp);

        var month = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
        var credit = MakeTx(2000m, "credit", "від мама", date: month.AddDays(1));
        var debit = MakeTx(12000m, "debit", "мама підтримка", date: month.AddDays(10));

        var result = await sut.ClassifyAsync(UserId, [credit, debit], UahAccount);

        var flow = result.MonthlyFlows.Should().ContainSingle().Subject;
        flow.OutflowUsd.Should().BeApproximately(CurrencyConverter.ToUsd(12000m, "UAH"), 0.01m);
        flow.InflowUsd.Should().BeApproximately(CurrencyConverter.ToUsd(2000m, "UAH"), 0.01m);
    }

    // ── T044-06: Equal credits and debits do NOT cancel each other ────────────

    [Fact]
    public async Task Classify_EqualCreditsAndDebits_NeitherDirectionIsCancelled()
    {
        var cp = MakeCounterparty("Мама", ("description_contains", "мама"));
        var sut = BuildSut(cp);

        var month = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
        var credit = MakeTx(10000m, "credit", "від мама", date: month.AddDays(1));
        var debit = MakeTx(10000m, "debit", "мама повернення", date: month.AddDays(5));

        var result = await sut.ClassifyAsync(UserId, [credit, debit], UahAccount);

        var flow = result.MonthlyFlows.Should().ContainSingle().Subject;
        var expectedUsd = CurrencyConverter.ToUsd(10000m, "UAH");
        flow.InflowUsd.Should().BeApproximately(expectedUsd, 0.01m);
        flow.OutflowUsd.Should().BeApproximately(expectedUsd, 0.01m);
    }

    // ── T044-07: Multi-counterparty in the same month ─────────────────────────

    [Fact]
    public async Task Classify_MultipleCounterpartiesSameMonth_EachBucketsSeparately()
    {
        var mama = MakeCounterparty("Мама", ("description_contains", "мама"));
        var liza = MakeCounterparty("Ліза", ("description_contains", "Ліза"));
        var sut = BuildSut(mama, liza);

        var month = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
        var mamaCredit = MakeTx(5000m, "credit", "від мама", date: month.AddDays(1));
        var lizaDebit = MakeTx(3000m, "debit", "Ліза допомога", date: month.AddDays(2));

        var result = await sut.ClassifyAsync(UserId, [mamaCredit, lizaDebit], UsdAccount);

        result.MatchedTransactionIds.Should().BeEquivalentTo([mamaCredit.Id, lizaDebit.Id]);
        result.MonthlyFlows.Should().HaveCount(2);
        result.MonthlyFlows.Should().ContainSingle(f => f.CounterpartyName == "Мама" && f.InflowUsd == 5000m);
        result.MonthlyFlows.Should().ContainSingle(f => f.CounterpartyName == "Ліза" && f.OutflowUsd == 3000m);
    }

    // ── T044-08: Multi-month window produces per-month results ────────────────

    [Fact]
    public async Task Classify_MultiMonthWindow_ProducesPerMonthRows()
    {
        var cp = MakeCounterparty("Мама", ("description_contains", "мама"));
        var sut = BuildSut(cp);

        // July: pure debit (expense). August: pure credit (income).
        var julyDebit = MakeTx(8000m, "debit", "мама липень",
            date: new DateTime(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc));
        var augustCredit = MakeTx(18000m, "credit", "від мама серпень",
            date: new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc));

        var result = await sut.ClassifyAsync(UserId, [julyDebit, augustCredit], UsdAccount);

        result.MonthlyFlows.Should().HaveCount(2);
        var july = result.MonthlyFlows.Single(f => f.Month == "2026-07");
        july.OutflowUsd.Should().Be(8000m);
        july.InflowUsd.Should().Be(0m);

        var august = result.MonthlyFlows.Single(f => f.Month == "2026-08");
        august.InflowUsd.Should().Be(18000m);
        august.OutflowUsd.Should().Be(0m);
    }

    // ── T044-09: First-match wins when multiple counterparties could match ─────

    [Fact]
    public async Task Classify_FirstMatchWins_TransactionNotDoubleMatched()
    {
        // Both counterparties have a rule that matches "мама"
        var cp1 = MakeCounterparty("Counterparty1", ("description_contains", "мама"));
        var cp2 = MakeCounterparty("Counterparty2", ("description_contains", "мама"));
        var sut = BuildSut(cp1, cp2);

        var tx = MakeTx(1000m, "debit", "мама test");
        var result = await sut.ClassifyAsync(UserId, [tx], UsdAccount);

        // Matched exactly once
        result.MatchedTransactionIds.Should().Contain(tx.Id);
        result.MonthlyFlows.Should().HaveCount(1);
        result.MonthlyFlows[0].CounterpartyName.Should().Be("Counterparty1"); // first match
    }

    // ── T044-10: Case-insensitive matching ────────────────────────────────────

    [Fact]
    public async Task Classify_PatternMatchIsCaseInsensitive()
    {
        var cp = MakeCounterparty("Мама", ("description_contains", "МАМА"));
        var sut = BuildSut(cp);

        var tx = MakeTx(1000m, "credit", "від мама сичова");
        var result = await sut.ClassifyAsync(UserId, [tx], UsdAccount);

        result.MatchedTransactionIds.Should().Contain(tx.Id);
    }

    // ── T044-11: Empty inputs return empty result ─────────────────────────────

    [Fact]
    public async Task Classify_EmptyTransactionList_ReturnsEmpty()
    {
        var cp = MakeCounterparty("Мама", ("description_contains", "мама"));
        var sut = BuildSut(cp);

        var result = await sut.ClassifyAsync(UserId, [], UsdAccount);

        result.MatchedTransactionIds.Should().BeEmpty();
        result.MonthlyFlows.Should().BeEmpty();
    }

    // ── T044-12: Inactive transactions are ignored ────────────────────────────

    [Fact]
    public async Task Classify_InactiveTransaction_IsIgnored()
    {
        var cp = MakeCounterparty("Мама", ("description_contains", "мама"));
        var sut = BuildSut(cp);

        var tx = MakeTx(1000m, "credit", "від мама");
        tx.IsActive = false;

        var result = await sut.ClassifyAsync(UserId, [tx], UsdAccount);

        result.MatchedTransactionIds.Should().BeEmpty();
        result.MonthlyFlows.Should().BeEmpty();
    }

    // ── T044-13: Flow role rides through to the monthly flow ──────────────────

    [Fact]
    public async Task Classify_InvestmentCounterparty_CarriesInvestmentFlowRole()
    {
        var cp = MakeCounterparty("Investment routing", FlowRoles.Investment, ("merchant_name_contains", "Binance"));
        var sut = BuildSut(cp);

        var tx = MakeTx(500m, "debit", "Card payment", merchantName: "BINANCE");

        var result = await sut.ClassifyAsync(UserId, [tx], UsdAccount);

        result.MonthlyFlows.Should().ContainSingle()
              .Which.Should().BeEquivalentTo(new
              {
                  FlowRole = FlowRoles.Investment,
                  OutflowUsd = 500m,
                  InflowUsd = 0m,
              });
    }

    // ── T044-14: Re-running over the same input produces the same buckets ─────

    [Fact]
    public async Task Classify_RerunOverSameInput_ProducesIdenticalOrderedFlows()
    {
        var mama = MakeCounterparty("Мама", ("description_contains", "мама"));
        var liza = MakeCounterparty("Ліза", ("description_contains", "Ліза"));
        var sut = BuildSut(mama, liza);

        var july = new DateTime(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc);
        var august = new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc);
        var batch = new List<Transaction>
        {
            MakeTx(3000m, "debit", "Ліза допомога", date: august),
            MakeTx(18000m, "credit", "від мама", date: august),
            MakeTx(8000m, "debit", "мама липень", date: july),
        };

        var first = await sut.ClassifyAsync(UserId, batch, UsdAccount);
        var second = await sut.ClassifyAsync(UserId, batch, UsdAccount);

        second.MonthlyFlows.Should().Equal(first.MonthlyFlows);
        second.MatchedTransactionIds.Should().BeEquivalentTo(first.MatchedTransactionIds);
    }

    // ── T044-15: Window path loads its own transactions and currencies ────────

    [Fact]
    public async Task ClassifyForWindow_LoadsTransactionsAndAccountCurrencies()
    {
        // The single entry point every consumer shares: it must resolve the account currency
        // itself, or a UAH statement would be counted as if it were dollars.
        var account = MakeAccount("UAH");
        var cp = MakeCounterparty("Мама", ("description_contains", "мама"));

        var rent = MakeTx(18000m, "credit", "від мама", accountId: account.Id);
        var sut = BuildSutForWindow([rent], account, cp);

        var result = await sut.ClassifyForWindowAsync(UserId, months: 6);

        result.MatchedTransactionIds.Should().ContainSingle();
        result.MonthlyFlows.Should().ContainSingle()
              .Which.InflowUsd.Should().Be(CurrencyConverter.ToUsd(18000m, "UAH"));
    }

    // ── PR #547 review: only explicit credit/debit is classified ──────────────

    [Fact]
    public async Task Classify_NullTransactionType_IsIgnored()
    {
        // Direction can't be guessed: a null (or unknown) TransactionType used to fall into
        // the else-branch and count as OUTFLOW. It must be skipped entirely — the same
        // "credit"/"debit" convention MoneyFlowStatisticsService sums by.
        var cp = MakeCounterparty("Мама", ("description_contains", "мама"));
        var sut = BuildSut(cp);

        var typed = MakeTx(1000m, "credit", "від мама");
        var untyped = MakeTx(5000m, "credit", "переказ мама");
        untyped.TransactionType = null;

        var result = await sut.ClassifyAsync(UserId, [typed, untyped], UahAccount);

        result.MatchedTransactionIds.Should().BeEquivalentTo([typed.Id]);
        var flow = result.MonthlyFlows.Should().ContainSingle().Subject;
        flow.InflowUsd.Should().Be(CurrencyConverter.ToUsd(1000m, "UAH"));
        flow.OutflowUsd.Should().Be(0m);
    }

    // ── PR #547 review: window classification runs once per request scope ─────

    [Fact]
    public async Task ClassifyForWindow_SecondCallInSameScope_ReturnsMemoizedResultWithoutReclassifying()
    {
        // FR-006/FR-010: one classification per request, shared by every consumer. The
        // standalone money-flow and top-categories handlers each call this entry point, so
        // the scoped service must hand the second caller the first call's result.
        var account = MakeAccount("UAH");
        var cp = MakeCounterparty("Мама", ("description_contains", "мама"));

        var repoMock = new Mock<ICounterpartyRepository>();
        repoMock.Setup(r => r.GetForUserAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([cp]);
        var txMock = new Mock<ITransactionRepository>();
        txMock.Setup(r => r.GetByUserIdSinceAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync([MakeTx(18000m, "credit", "від мама", accountId: account.Id)]);
        var acctMock = new Mock<IBankAccountRepository>();
        acctMock.Setup(r => r.GetByUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([account]);

        var sut = new CounterpartyClassificationService(repoMock.Object, txMock.Object, acctMock.Object);

        var first = await sut.ClassifyForWindowAsync(UserId, months: 6);
        var second = await sut.ClassifyForWindowAsync(UserId, months: 6);

        second.Should().BeSameAs(first);
        txMock.Verify(r => r.GetByUserIdSinceAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
        repoMock.Verify(r => r.GetForUserAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── PR #547 review: the seeded "мама" pattern overmatches — documented ────

    [Fact]
    public async Task Classify_GenericMamaPatternInUnrelatedDescription_IsStillMatched_KnownOvermatchRisk()
    {
        // REGRESSION-DOCUMENTING TEST, not an endorsement. M011 seeds a description_contains
        // rule for the bare word "мама", and matching is case-insensitive substring — so a
        // card payment at a business whose name merely CONTAINS the word (here a restaurant)
        // is classified as family support. That is the deployed semantics today; if this
        // test ever fails, the matching behavior changed and the seed rules must be re-audited
        // (a tighter pattern or a word-boundary match would be the fix).
        var cp = MakeCounterparty("Мама", ("description_contains", "мама"));
        var sut = BuildSut(cp);

        var restaurant = MakeTx(850m, "debit", "Ресторан Мама Манана, Київ");

        var result = await sut.ClassifyAsync(UserId, [restaurant], UahAccount);

        result.MatchedTransactionIds.Should().Contain(restaurant.Id,
            "the seeded substring rule cannot tell the family member from a business name containing the word");
        result.MonthlyFlows.Should().ContainSingle()
              .Which.OutflowUsd.Should().Be(CurrencyConverter.ToUsd(850m, "UAH"));
    }

    // ── #580: currency-scoped rules and the self-routing role ─────────────────

    [Fact]
    public async Task Classify_CurrencyScopedRule_BeatsGenericRuleForTheSameText()
    {
        // «Від: Людмила Сичова» on a EUR account is the user's own money mid-route; the same
        // wording on a UAH account is rent. The EUR-scoped self-routing rule must win on the
        // EUR account even though the generic family rule matches the text too.
        var family = MakeCounterparty("Мама", ("description_contains", "Людмила Сичова"));
        var routing = MakeCounterparty("Routing via mom (EUR)", FlowRoles.SelfRouting);
        routing.Rules.Add(new CounterpartyRule
        {
            CounterpartyId = routing.Id,
            MatchType = "description_contains",
            Pattern = "Від: Людмила Сичова",
            Currency = "EUR",
        });

        // Family listed FIRST — ordering must not decide; specificity must.
        var sut = BuildSut(family, routing);
        var eurCredit = MakeTx(1200m, "credit", "Від: Людмила Сичова");
        var eurAccount = new Dictionary<Guid, string> { [AccountId] = "EUR" };

        var result = await sut.ClassifyAsync(UserId, [eurCredit], eurAccount);

        result.Matches![eurCredit.Id].Name.Should().Be("Routing via mom (EUR)");
        result.Matches[eurCredit.Id].FlowRole.Should().Be(FlowRoles.SelfRouting);
    }

    [Fact]
    public async Task Classify_CurrencyScopedRule_DoesNotMatchOtherCurrencies()
    {
        // The UAH rent keeps matching the generic family rule when the EUR-scoped routing
        // rule exists — the scope must never leak across currencies.
        var family = MakeCounterparty("Мама", ("description_contains", "Людмила Сичова"));
        var routing = MakeCounterparty("Routing via mom (EUR)", FlowRoles.SelfRouting);
        routing.Rules.Add(new CounterpartyRule
        {
            CounterpartyId = routing.Id,
            MatchType = "description_contains",
            Pattern = "Від: Людмила Сичова",
            Currency = "EUR",
        });

        var sut = BuildSut(routing, family);
        var uahRent = MakeTx(18000m, "credit", "Від: Людмила Сичова");

        var result = await sut.ClassifyAsync(UserId, [uahRent], UahAccount);

        result.Matches![uahRent.Id].Name.Should().Be("Мама");
        result.Matches[uahRent.Id].FlowRole.Should().Be(FlowRoles.FamilySupport);
    }

    // ── #434 S1: native per-currency subtotals on CounterpartyMonthlyFlow ──────

    [Fact]
    public async Task Classify_CounterpartyWithTwoAccountCurrencies_ProducesTwoNativeSubtotalsSummingToUsdTotal()
    {
        var mama = MakeCounterparty("Мама", ("description_contains", "мама"));
        var sut = BuildSut(mama);

        var uahAccountId = Guid.NewGuid();
        var eurAccountId = Guid.NewGuid();
        var uahCredit = MakeTx(18000m, "credit", "від мама", accountId: uahAccountId);
        var eurDebit = MakeTx(200m, "debit", "мама переказ", accountId: eurAccountId);
        var accountCurrencies = new Dictionary<Guid, string> { [uahAccountId] = "UAH", [eurAccountId] = "EUR" };

        var result = await sut.ClassifyAsync(UserId, [uahCredit, eurDebit], accountCurrencies);

        var flow = result.MonthlyFlows.Should().ContainSingle().Subject;
        flow.ByCurrency.Should().HaveCount(2);

        var uah = flow.ByCurrency!.Single(c => c.Currency == "UAH");
        uah.Received.Should().Be(18000m);
        uah.Sent.Should().Be(0m);

        var eur = flow.ByCurrency!.Single(c => c.Currency == "EUR");
        eur.Received.Should().Be(0m);
        eur.Sent.Should().Be(200m);

        CurrencyConverter.ToUsd(uah.Received, "UAH")
            .Should().BeApproximately(flow.InflowUsd, 0.01m);
        CurrencyConverter.ToUsd(eur.Sent, "EUR")
            .Should().BeApproximately(flow.OutflowUsd, 0.01m);
    }

    [Fact]
    public async Task Classify_SingleCurrencyCounterparty_ProducesOneNativeSubtotal()
    {
        var mama = MakeCounterparty("Мама", ("description_contains", "мама"));
        var sut = BuildSut(mama);

        var credit = MakeTx(5000m, "credit", "від мама");
        var result = await sut.ClassifyAsync(UserId, [credit], UsdAccount);

        var flow = result.MonthlyFlows.Should().ContainSingle().Subject;
        var only = flow.ByCurrency.Should().ContainSingle().Subject;
        only.Currency.Should().Be("USD");
        only.Received.Should().Be(5000m);
        only.Sent.Should().Be(0m);
    }
}
