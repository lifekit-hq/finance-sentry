namespace FinanceSentry.Tests.Integration.CrossModulePorts;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Integration;
using FinanceSentry.Modules.Alerts.API.Responses;
using FinanceSentry.Modules.Alerts.Application.Queries;
using FinanceSentry.Modules.Alerts.Application.Services;
using FinanceSentry.Modules.BrokerageSync.Application.Queries;
using FinanceSentry.Modules.BrokerageSync.Application.Services;
using FinanceSentry.Modules.Radar.Application.Queries;
using FinanceSentry.Modules.Radar.Application.Services;
using FinanceSentry.Modules.Radar.Domain.MarketStructure;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Risk.Application.Services;
using FinanceSentry.Modules.Risk.Domain;
using FinanceSentry.Modules.Risk.Domain.Repositories;
using FinanceSentry.Modules.Wealth.Application.Services;
using FinanceSentry.Modules.Wealth.Domain;
using FinanceSentry.Modules.Wealth.Domain.Repositories;
using FluentAssertions;
using Moq;
using Xunit;

/// <summary>
/// #673: each Integration adapter below reaches its owning module only through that module's
/// published Domain.Ports read port. These compose the adapter over the real port impl, so the pair
/// is pinned against the same module internals the adapter used to read directly.
/// </summary>
public sealed class ReadPortCompositionTests
{
    private static readonly Guid User = Guid.NewGuid();

    [Fact]
    public async Task Asset_signals_read_the_subjects_last_30_days_newest_first()
    {
        ListSignalsQuery? captured = null;
        var now = DateTimeOffset.UtcNow;
        var handler = new Mock<IQueryHandler<ListSignalsQuery, IReadOnlyList<RadarSignalDto>>>();
        handler.Setup(h => h.Handle(It.IsAny<ListSignalsQuery>(), It.IsAny<CancellationToken>()))
            .Callback<ListSignalsQuery, CancellationToken>((q, _) => captured = q)
            .ReturnsAsync([
                Signal(now.AddDays(-3), "old"),
                Signal(now.AddDays(-1), "newest"),
                Signal(now.AddDays(-2), "middle"),
            ]);

        var rows = await new AssetSignalAdapter(new SubjectSignalReader(handler.Object)).GetRecentAsync("MU", 2);

        captured.Should().NotBeNull();
        captured!.Subject.Should().Be("MU");
        captured.Since.Should().Be(DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)));
        captured.Scanner.Should().BeNull();
        captured.Type.Should().BeNull();
        captured.UserId.Should().BeNull();
        rows.Select(r => r.SignalType).Should().Equal("newest", "middle");
        rows[0].Payload.Should().ContainKey("k");
    }

    [Fact]
    public async Task Fired_alerts_pass_the_page_request_through_and_keep_the_total()
    {
        GetAlertsByTypesQuery? captured = null;
        var created = DateTimeOffset.UtcNow;
        var referenceId = Guid.NewGuid();
        var alert = new AlertDto(
            Guid.NewGuid(), "PriceMove", "Warning", "MU moved", "MU up 9%", referenceId, "MU",
            true, false, created, null, 1, created);
        var handler = new Mock<IQueryHandler<GetAlertsByTypesQuery, AlertsByTypePage>>();
        handler.Setup(h => h.Handle(It.IsAny<GetAlertsByTypesQuery>(), It.IsAny<CancellationToken>()))
            .Callback<GetAlertsByTypesQuery, CancellationToken>((q, _) => captured = q)
            .ReturnsAsync(new AlertsByTypePage([alert], 41));

        var page = await new EventsFiredAlertAdapter(new AlertsByTypeReader(handler.Object))
            .ListAsync(User, ["PriceMove"], 3, 20);

        captured.Should().NotBeNull();
        captured!.UserId.Should().Be(User);
        captured.Types.Should().Equal("PriceMove");
        captured.Page.Should().Be(3);
        captured.PageSize.Should().Be(20);
        page.TotalCount.Should().Be(41);
        var row = page.Items.Should().ContainSingle().Subject;
        row.AlertId.Should().Be(alert.Id);
        row.Type.Should().Be("PriceMove");
        row.Severity.Should().Be("Warning");
        row.Title.Should().Be("MU moved");
        row.Message.Should().Be("MU up 9%");
        row.ReferenceId.Should().Be(referenceId);
        row.ReferenceLabel.Should().Be("MU");
        row.IsRead.Should().BeTrue();
        row.IsResolved.Should().BeFalse();
        row.CreatedAt.Should().Be(created);
    }

    [Fact]
    public async Task Macro_events_read_every_region_and_importance_in_the_window()
    {
        var from = new DateOnly(2026, 10, 1);
        var to = new DateOnly(2026, 10, 31);
        var cpi = new MacroEvent
        {
            EventDate = new DateOnly(2026, 10, 14),
            EventTime = new TimeOnly(12, 30),
            Event = "CPI",
            Region = "US",
            Importance = MacroImportance.High,
            Source = "seed",
        };
        var macro = new Mock<IMacroCalendarService>();
        macro.Setup(m => m.QueryAsync(from, to, null, null, It.IsAny<CancellationToken>())).ReturnsAsync([cpi]);

        var rows = await new EventsMacroEventAdapter(new MacroCalendarReader(macro.Object)).QueryAsync(from, to);

        var row = rows.Should().ContainSingle().Subject;
        row.Id.Should().Be(cpi.Id);
        row.Date.Should().Be(cpi.EventDate);
        row.Time.Should().Be(cpi.EventTime);
        row.Event.Should().Be("CPI");
        row.Region.Should().Be("US");
        row.Importance.Should().Be(MacroImportance.High);
        row.Source.Should().Be("seed");
    }

    [Fact]
    public async Task Tax_lots_match_the_symbol_case_insensitively()
    {
        var acquired = new DateTime(2025, 1, 15, 0, 0, 0, DateTimeKind.Utc);
        var handler = TaxLotsHandler(
            Lot("MU", 10m, acquired),
            Lot("mu", 5m, null),
            Lot("NVDA", 7m, null));

        var rows = await new HoldingTaxLotsAdapter(new TaxLotReader(handler)).GetForSymbolAsync(User, "Mu");

        rows.Should().NotBeNull();
        rows!.Select(r => r.Quantity).Should().Equal(10m, 5m);
        var first = rows[0];
        first.CurrentValueUsd.Should().Be(1000m);
        first.AverageCostUsd.Should().Be(80m);
        first.CostBasisUsd.Should().Be(800m);
        first.UnrealizedPnlUsd.Should().Be(200m);
        first.UnrealizedPnlPercent.Should().Be(25m);
        first.AcquiredAt.Should().Be(acquired);
        first.IsLongTerm.Should().BeTrue();
    }

    [Fact]
    public async Task Tax_lots_are_null_when_the_user_holds_no_lot_in_the_symbol()
    {
        var handler = TaxLotsHandler(Lot("NVDA", 7m, null));

        var rows = await new HoldingTaxLotsAdapter(new TaxLotReader(handler)).GetForSymbolAsync(User, "MU");

        rows.Should().BeNull();
    }

    [Fact]
    public async Task Portfolio_value_reads_the_brokerage_sleeve_of_each_snapshot()
    {
        var from = new DateOnly(2026, 9, 1);
        var to = new DateOnly(2026, 9, 2);
        var snapshots = new Mock<INetWorthSnapshotRepository>();
        snapshots.Setup(s => s.GetByUserIdUnscopedAsync(User, from, to, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new NetWorthSnapshot { UserId = User, SnapshotDate = from, BankingTotal = 5m, BrokerageTotal = 100m, TotalNetWorth = 105m },
                new NetWorthSnapshot { UserId = User, SnapshotDate = to, BankingTotal = 5m, BrokerageTotal = 110m, TotalNetWorth = 115m },
            ]);

        var rows = await new RadarPortfolioValueSource(new BrokerageValueHistoryReader(snapshots.Object))
            .GetAsync(User, from, to);

        rows.Select(r => (r.Date, r.BrokerageValueUsd)).Should().Equal((from, 100m), (to, 110m));
    }

    [Fact]
    public async Task Position_cap_is_the_current_rule_sets_max_position_weight()
    {
        var ruleSets = new Mock<IRiskRuleSetRepository>();
        ruleSets.Setup(r => r.GetCurrentUnscopedAsync(User, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RiskRuleSet { UserId = User, MaxPositionWeightPct = 0.15m, MinCashBufferPct = 0.05m });

        var cap = await new RiskPositionCapSource(new RiskLimitsReader(ruleSets.Object))
            .GetMaxPositionWeightAsync(User, CancellationToken.None);

        cap.Should().Be(0.15m);
    }

    [Fact]
    public async Task Position_cap_is_null_without_a_rule_set()
    {
        var ruleSets = new Mock<IRiskRuleSetRepository>();
        ruleSets.Setup(r => r.GetCurrentUnscopedAsync(User, It.IsAny<CancellationToken>())).ReturnsAsync((RiskRuleSet?)null);

        var cap = await new RiskPositionCapSource(new RiskLimitsReader(ruleSets.Object))
            .GetMaxPositionWeightAsync(User, CancellationToken.None);

        cap.Should().BeNull();
    }

    private static RadarSignalDto Signal(DateTimeOffset timestamp, string signalType)
        => new(timestamp, "momentum", signalType, "info", "ticker", "MU", $"dedup-{signalType}",
            new Dictionary<string, object> { ["k"] = 1 }, 1);

    private static TaxLotDto Lot(string symbol, decimal quantity, DateTime? acquiredAt)
        => new(symbol, "STK", quantity, 1000m, 80m, 800m, 200m, 25m, acquiredAt, true, "Verified");

    private static IQueryHandler<GetTaxLotsQuery, TaxLotsResponse> TaxLotsHandler(params TaxLotDto[] lots)
    {
        var handler = new Mock<IQueryHandler<GetTaxLotsQuery, TaxLotsResponse>>();
        handler.Setup(h => h.Handle(new GetTaxLotsQuery(User), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TaxLotsResponse("IBKR", null, lots, 0m, 0m));
        return handler.Object;
    }
}
