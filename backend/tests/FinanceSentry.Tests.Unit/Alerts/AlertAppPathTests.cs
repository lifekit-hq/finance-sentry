namespace FinanceSentry.Tests.Unit.Alerts;

using System.Reflection;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Utils;
using FinanceSentry.Modules.Alerts.Application.Services;
using FinanceSentry.Modules.Alerts.Domain;
using FinanceSentry.Modules.Alerts.Domain.Repositories;
using FinanceSentry.Modules.Risk.Domain;
using FluentAssertions;
using Moq;
using Xunit;

/// <summary>#466 N-A: every alert opens the thing it is about, on routes and URL filters that already exist.</summary>
public class AlertAppPathTests
{
    private static readonly Guid Account = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly DateTimeOffset March14 = new(2026, 3, 14, 9, 30, 0, TimeSpan.Zero);

    /// <summary>Alert types with no page in the app that shows what they are about.</summary>
    private static readonly HashSet<string> NoTarget = new(StringComparer.Ordinal) { AlertType.JobFailure };

    private static readonly string[] AllAlertTypes = typeof(AlertType)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.IsLiteral)
        .Select(f => (string)f.GetRawConstantValue()!)
        .ToArray();

    public static TheoryData<string, Guid?, string?, string?> Table => new()
    {
        // N-A rows: A-1 / A-2 open the account's ledger.
        { AlertType.LowBalance, Account, "Chase", $"/transactions?account={Account}" },
        { AlertType.CashShortfall, Account, "Chase", $"/transactions?account={Account}" },
        { AlertType.LowBalance, null, "Chase", "/accounts/list" },

        // A-5: the month's debits in the category. A-6: the breach month's, closed.
        { AlertType.CategorySpike, Guid.NewGuid(), "Dining out", "/transactions?type=debit&category=Dining%20out&from=2026-03-01" },
        { AlertType.BudgetBreach, Guid.NewGuid(), "Dining out", "/transactions?type=debit&category=Dining%20out&from=2026-03-01&to=2026-03-31" },
        { AlertType.CategorySpike, Guid.NewGuid(), null, "/transactions" },

        // A-8 interim: the debit leg sits inside the detection lookback.
        { AlertType.FxSpread, Guid.NewGuid(), "EUR to USD", "/transactions?type=debit&from=2026-03-11&to=2026-03-14" },

        // A-9 / A-10 without the emitter's knowledge fall back to the overview.
        { AlertType.PolicyViolation, Guid.NewGuid(), "NVDA", "/accounts/investments" },
        { AlertType.RelativeUnderperformance, Guid.NewGuid(), "NVDA", "/accounts/investments" },

        // Ticker-labelled research alerts open the dossier; a label that is no symbol (feed freshness) opens nothing.
        { AlertType.ThesisBroken, Guid.NewGuid(), "NVDA", "/assets/NVDA" },
        { AlertType.MarketStructure, Guid.NewGuid(), "BRK.B", "/assets/BRK.B" },
        { AlertType.Opportunity, Guid.NewGuid(), "BTC", "/assets/BTC" },
        { AlertType.EarningsAhead, Guid.NewGuid(), "AAPL", "/assets/AAPL" },
        { AlertType.FilingLanded, Guid.NewGuid(), "MSFT", "/assets/MSFT" },
        { AlertType.NewsCluster, Guid.NewGuid(), "TSLA", "/assets/TSLA" },
        { AlertType.MarketStructure, Guid.NewGuid(), "freshness", null },
        { AlertType.ThesisBroken, null, null, null },

        // Singletons keep the page that already holds them.
        { AlertType.SyncFailure, Account, "Monobank", "/accounts/list" },
        { AlertType.ConsentExpiring, Account, "AIB", "/accounts/list" },
        { AlertType.FamilyStatement, null, null, "/accounts/list" },
        { AlertType.PriceHike, Guid.NewGuid(), "Netflix", "/subscriptions" },
        { AlertType.DuplicateCharge, Guid.NewGuid(), "starbucks", "/transactions" },
        { AlertType.UnusualSpend, Guid.NewGuid(), "Groceries", "/transactions" },
        { AlertType.RebalanceProposal, null, null, "/accounts/investments" },
        { AlertType.CashSweepProposal, null, null, "/accounts/investments" },
        { AlertType.PolicyReview, null, null, "/accounts/investments" },
        { AlertType.PolicyReviewMissed, null, null, "/accounts/investments" },
        { AlertType.PerformanceBrief, null, null, "/accounts/investments" },
        { AlertType.FireBrief, null, null, "/dashboard" },

        // N-8: a job has no page in the app.
        { AlertType.JobFailure, null, "SyncJob", null },
        { "SomethingNew", Guid.NewGuid(), "x", null },
    };

    [Theory]
    [MemberData(nameof(Table))]
    public void Resolve_gives_each_alert_type_its_page(string type, Guid? referenceId, string? label, string? expected)
        => AlertAppPath.Resolve(type, referenceId, label, March14).Should().Be(expected);

    [Fact]
    public void Every_alert_type_has_a_row_in_the_table_or_is_listed_as_having_no_target()
    {
        var covered = Table.Select(row => (string)row[0]).ToHashSet();

        AllAlertTypes.Where(t => !covered.Contains(t) && !NoTarget.Contains(t)).Should().BeEmpty(
            "a new alert type must be given a path in AlertAppPath.Resolve and a row in this table, or be listed in NoTarget");
        AlertAppPath.Resolve(AlertType.JobFailure, null, null, March14).Should().BeNull();
    }

    [Fact]
    public void Every_resolved_path_is_a_relative_in_app_path_without_a_host()
    {
        foreach (var row in Table)
        {
            var path = AlertAppPath.Resolve((string)row[0], (Guid?)row[1], (string?)row[2], March14);
            if (path is not null)
            {
                path.Should().StartWith("/").And.NotStartWith("//").And.NotContain("://");
            }
        }
    }

    [Fact]
    public void A_label_with_url_syntax_cannot_break_out_of_its_filter_value()
        => AlertAppPath.Resolve(AlertType.CategorySpike, null, "A&B=C#x", March14)
            .Should().Be("/transactions?type=debit&category=A%26B%3DC%23x&from=2026-03-01");

    [Fact]
    public void The_month_comes_from_the_alert_in_UTC()
        => AlertAppPath.Resolve(AlertType.BudgetBreach, null, "Rent", new DateTimeOffset(2026, 2, 28, 23, 30, 0, TimeSpan.FromHours(-2)))
            .Should().Be("/transactions?type=debit&category=Rent&from=2026-03-01&to=2026-03-31");

    [Theory]
    [InlineData(RiskRuleKeys.MaxPositionWeight, "NVDA", false, "/assets/NVDA")]
    [InlineData(RiskRuleKeys.AddToBrokenThesis, "NVDA", false, "/assets/NVDA")]
    [InlineData(RiskRuleKeys.MaxSleeveWeight, "GROWTH", false, "/accounts/investments")]
    [InlineData(RiskRuleKeys.MinCashBuffer, "BOOK", false, "/accounts/investments")]
    [InlineData(RiskRuleKeys.MaxDrawdown, RiskRuleKeys.BookSubject, false, "/accounts/investments")]
    [InlineData(RiskRuleKeys.Turnover, "2026Q1", false, "/accounts/investments")]
    [InlineData(RiskRuleKeys.AllocationDrift, "EQUITY", false, "/accounts/investments")]
    [InlineData(RiskRuleKeys.MaxNewPosition, "AMD", true, "/assets/AMD")]
    [InlineData(RiskRuleKeys.MaxPositionWeight, "not a symbol", false, "/accounts/investments")]
    public void A_policy_violation_opens_the_dossier_only_when_its_rule_is_about_one_holding(
        string ruleKey, string subject, bool isOverride, string expected)
        => AlertAppPath.ForPolicyViolation(ruleKey, subject, isOverride).Should().Be(expected);

    [Fact]
    public void Every_risk_rule_key_is_decided_by_the_rule_not_by_the_shape_of_its_subject()
    {
        var ruleKeys = typeof(RiskRuleKeys).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(f => (string)f.GetRawConstantValue()!)
            .Where(k => k != RiskRuleKeys.BookSubject);

        // A ticker-shaped subject on a book- or sleeve-level rule must not turn into a dossier link.
        var openingADossier = ruleKeys.Where(k => AlertAppPath.ForPolicyViolation(k, "NVDA", false).StartsWith("/assets/"));

        openingADossier.Should().BeEquivalentTo(RiskRuleKeys.MaxPositionWeight, RiskRuleKeys.AddToBrokenThesis);
    }

    [Theory]
    [InlineData("Thesis", "NVDA", "/assets/NVDA")]
    [InlineData("thesis", "NVDA", "/assets/NVDA")]
    [InlineData("Book", "BOOK", "/accounts/investments")]
    [InlineData("Sleeve", "GROWTH", "/accounts/investments")]
    [InlineData("Thesis", "not a symbol", "/accounts/investments")]
    public void A_thesis_underperformance_opens_its_ticker_and_the_book_or_a_sleeve_open_the_overview(
        string scope, string label, string expected)
        => AlertAppPath.ForRelativeUnderperformance(scope, label).Should().Be(expected);

    [Fact]
    public void A_budget_breach_opens_the_breach_month_not_the_month_it_was_raised_in()
        => AlertAppPath.ForBudgetBreach("Dining out", 2026, 2)
            .Should().Be("/transactions?type=debit&category=Dining%20out&from=2026-02-01&to=2026-02-28");

    [Theory]
    [InlineData("NVDA", "/assets/NVDA")]
    [InlineData("nvda", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("../admin", null)]
    public void ForSymbol_only_accepts_symbol_shaped_values(string? symbol, string? expected)
        => AlertAppPath.ForSymbol(symbol).Should().Be(expected);

    [Fact]
    public async Task The_generator_stores_the_path_its_emitter_resolved()
    {
        var repo = new Mock<IAlertRepository>();
        var service = new AlertGeneratorService(repo.Object, Mock.Of<IPolicyAckReader>());
        var user = Guid.NewGuid();
        var saved = new List<Alert>();
        repo.Setup(r => r.AddAsync(It.IsAny<Alert>(), default)).Callback<Alert, CancellationToken>((a, _) => saved.Add(a));

        await service.GeneratePolicyViolationAlertAsync(user, RiskRuleKeys.MaxPositionWeight, "NVDA", 30m, 25m);
        await service.GeneratePolicyViolationAlertAsync(user, RiskRuleKeys.MaxSleeveWeight, "GROWTH", 60m, 50m);
        await service.GenerateRelativeUnderperformanceAlertAsync(
            user, "Thesis", "t-1", "AMD", "90d", "SPY", -4m, 3, 3m);
        await service.GenerateBudgetExceededAlertAsync(user, Guid.NewGuid(), "Dining out", 300m, 200m, 2026, 2);
        await service.GenerateLowBalanceAlertAsync(user, Account, "Chase", 10m, 100m);

        saved.Select(a => (a.Type, a.AppPath)).Should().Equal(
            (AlertType.PolicyViolation, "/assets/NVDA"),
            (AlertType.PolicyViolation, "/accounts/investments"),
            (AlertType.RelativeUnderperformance, "/assets/AMD"),
            (AlertType.BudgetBreach, "/transactions?type=debit&category=Dining%20out&from=2026-02-01&to=2026-02-28"),
            (AlertType.LowBalance, $"/transactions?account={Account}"));
    }

    [Fact]
    public void An_alert_stored_before_the_path_existed_resolves_from_what_it_carries()
    {
        var legacy = new Alert
        {
            Type = AlertType.ThesisBroken, ReferenceLabel = "NVDA", CreatedAt = March14,
        };
        var stored = new Alert { Type = AlertType.ThesisBroken, ReferenceLabel = "NVDA", AppPath = "/accounts/investments" };

        AlertAppPaths.For(legacy).Should().Be("/assets/NVDA");
        AlertAppPaths.For(stored).Should().Be("/accounts/investments");
    }
}
