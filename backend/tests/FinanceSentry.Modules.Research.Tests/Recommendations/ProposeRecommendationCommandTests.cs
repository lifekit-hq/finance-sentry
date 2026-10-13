namespace FinanceSentry.Modules.Research.Tests.Recommendations;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.Application.Commands;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Exceptions;
using FinanceSentry.Modules.Research.Domain.Ports;
using FinanceSentry.Modules.Research.Domain.Recommendations;
using FinanceSentry.Modules.Research.Domain.Repositories;
using FinanceSentry.Modules.Research.Tests.Opportunity;
using FluentAssertions;
using Moq;
using Xunit;

public sealed class ProposeRecommendationCommandTests
{
    private const decimal Cap = 0.095m;
    private static readonly Guid User = Guid.NewGuid();
    private static readonly IReadOnlyDictionary<string, object> NoPayload = new Dictionary<string, object>();

    private readonly InMemoryRecommendationRepository _repo = new();
    private readonly JournalRecorder _journal = new();
    private readonly Mock<IThesisRepository> _theses = new();
    private readonly Mock<IAllocationDriftReader> _drift = new();
    private readonly Mock<IAssetSignalReader> _signals = new();
    private readonly Mock<IFiredEventSource> _events = new();

    private ProposeRecommendationCommandHandler Handler(
        RiskGateVerdict? gate = null, decimal? cap = Cap, BookFigures? book = null)
    {
        _theses.Setup(t => t.FindByTickerAsync(User, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var figures = book ?? RecommendationTestBook.With(76_000m, RecommendationTestBook.Stock("GRAB", 2_962m, 9_272m));
        return new ProposeRecommendationCommandHandler(
            _theses.Object,
            new FakePositionCapSource(cap),
            _drift.Object,
            _signals.Object,
            _events.Object,
            new FakeRiskPolicyGate(gate ?? new RiskGateVerdict(RiskGateDecision.Allowed, null, null, null, null)),
            RecommendationTestBook.Reader(figures, new Dictionary<string, decimal> { ["GRAB"] = 3.13m, ["MSFT"] = 400m }),
            new RecommendationWriter(_repo, _journal));
    }

    private static ProposeRecommendationCommand Command(
        RecommendationAction action = RecommendationAction.Trim,
        RecommendationTriggerKind trigger = RecommendationTriggerKind.RiskRule,
        string ticker = "grab", string triggerRef = "", decimal? sizeUsd = null,
        IReadOnlyList<RecommendationEvidence>? evidence = null, int? expiresInDays = null)
        => new(User, ticker, action, trigger, triggerRef, "GRAB is 12.2% of book against a 9.5% cap.",
            RecommendationConfidence.High, "The position falls back under the cap.", evidence ?? [], sizeUsd, expiresInDays);

    [Fact]
    public async Task A_name_over_the_cap_is_trimmed_back_to_it_with_the_exact_order_text_and_a_journal_entry()
    {
        var dto = await Handler().Handle(Command(), CancellationToken.None);

        dto.Ticker.Should().Be("GRAB");
        dto.Status.Should().Be("Pending");
        dto.CreatedBy.Should().Be("Ledger");
        dto.Origin.Should().Be("Ledger");
        dto.SizeShares.Should().Be(656m);
        dto.ShareOfBookBeforePct.Should().Be(12.20m);
        dto.ShareOfBookAfterPct.Should().Be(9.50m);
        dto.OrderText.Should().Be("SELL 656 GRAB / LIMIT 3.13 USD DAY / IBKR");
        dto.TriggerRef.Should().Be(RecommendationEvidenceFactory.MaxPositionWeightRule);
        dto.Evidence[0].Kind.Should().Be(RecommendationEvidenceKind.RiskRule, "the trigger leads the evidence");
        dto.ExpiresAt.Should().BeCloseTo(DateTimeOffset.UtcNow.AddDays(ProposeRecommendationCommandHandler.DefaultExpiryDays), TimeSpan.FromMinutes(1));
        _journal.Events.Should().ContainSingle(e => e.SubjectType == ThesisSubjectType.Recommendation
            && e.EventType == ThesisEventType.Created && e.Note!.StartsWith("TRIM GRAB: "));
    }

    [Fact]
    public async Task A_newer_proposal_supersedes_the_pending_one_and_journals_it()
    {
        var handler = Handler();
        var first = await handler.Handle(Command(), CancellationToken.None);
        var second = await handler.Handle(
            Command(RecommendationAction.Exit, sizeUsd: null), CancellationToken.None);

        var old = _repo.Rows.Single(r => r.Id == first.Id);
        old.Status.Should().Be(RecommendationStatus.Superseded);
        old.SupersededById.Should().Be(second.Id);
        old.DecidedAt.Should().NotBeNull();
        _repo.Rows.Count(r => r.Status == RecommendationStatus.Pending).Should().Be(1);
        _journal.Events.Should().Contain(e => e.SubjectId == first.Id && e.EventType == ThesisEventType.Superseded);
    }

    [Fact]
    public async Task Hold_is_refused_unless_it_answers_a_fired_event()
    {
        var act = () => Handler().Handle(
            Command(RecommendationAction.Hold, RecommendationTriggerKind.RiskRule), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidRecommendationException>().WithMessage("*fired event*");
    }

    [Fact]
    public async Task Hold_answering_a_fired_event_moves_nothing_and_cites_the_event()
    {
        var eventId = Guid.NewGuid();
        _events.Setup(e => e.FindAsync(User, eventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FiredEventReference(eventId, "earnings", "GRAB Q3 results", DateTimeOffset.UtcNow));

        var dto = await Handler().Handle(
            Command(RecommendationAction.Hold, RecommendationTriggerKind.EventVerdict, triggerRef: eventId.ToString()),
            CancellationToken.None);

        dto.Action.Should().Be("Hold");
        dto.SizeUsd.Should().BeNull();
        dto.OrderText.Should().BeNull("a hold has no order");
        dto.Evidence[0].RefId.Should().Be(eventId.ToString());
    }

    [Fact]
    public async Task An_event_verdict_on_an_event_that_did_not_fire_for_the_owner_is_refused()
    {
        var act = () => Handler().Handle(
            Command(RecommendationAction.Hold, RecommendationTriggerKind.EventVerdict, triggerRef: Guid.NewGuid().ToString()),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidRecommendationException>();
    }

    [Fact]
    public async Task Trimming_a_name_that_is_not_held_is_refused()
    {
        var act = () => Handler().Handle(Command(ticker: "MSFT"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidRecommendationException>().WithMessage("*not held*");
    }

    [Fact]
    public async Task A_risk_rule_trigger_on_a_name_within_the_cap_is_refused()
    {
        var act = () => Handler(cap: 0.5m).Handle(Command(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidRecommendationException>().WithMessage("*not over*");
    }

    [Fact]
    public async Task A_thesis_break_trigger_needs_a_broken_thesis()
    {
        var act = () => Handler().Handle(
            Command(RecommendationAction.Exit, RecommendationTriggerKind.ThesisBreak), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidRecommendationException>().WithMessage("*broken thesis*");
    }

    [Fact]
    public async Task A_thesis_break_exit_sells_everything_and_cites_the_broken_thesis()
    {
        var handler = Handler();
        var thesis = new InvestmentThesis { UserId = User, Ticker = "GRAB", BrokenAt = DateTimeOffset.UtcNow, BrokenReason = "margin fell below 10%" };
        _theses.Setup(t => t.FindByTickerAsync(User, "GRAB", It.IsAny<CancellationToken>())).ReturnsAsync([thesis]);

        var dto = await handler.Handle(Command(RecommendationAction.Exit, RecommendationTriggerKind.ThesisBreak), CancellationToken.None);

        dto.SizeShares.Should().Be(2_962m);
        dto.TriggerRef.Should().Be(thesis.Id.ToString());
        dto.Evidence[0].Label.Should().Contain("margin fell below 10%");
    }

    [Fact]
    public async Task An_ips_band_trigger_must_name_a_sleeve_that_is_out_of_band_in_the_right_direction()
    {
        _drift.Setup(d => d.GetAsync(User, It.IsAny<CancellationToken>())).ReturnsAsync(new AllocationDriftReading(
            true, [new SleeveDriftReading("Equity", 60m, 72m, 12m, "OverBand")]));
        var handler = Handler();

        var trim = await handler.Handle(
            Command(trigger: RecommendationTriggerKind.IpsBand, triggerRef: "equity", sizeUsd: 1_000m), CancellationToken.None);
        trim.TriggerRef.Should().Be("Equity");

        var add = () => handler.Handle(
            Command(RecommendationAction.Add, RecommendationTriggerKind.IpsBand, triggerRef: "Equity", sizeUsd: 1_000m),
            CancellationToken.None);
        await add.Should().ThrowAsync<InvalidRecommendationException>().WithMessage("*OverBand*");
    }

    [Fact]
    public async Task An_ips_band_trigger_on_a_name_outside_the_sleeve_is_refused()
    {
        _drift.Setup(d => d.GetAsync(User, It.IsAny<CancellationToken>())).ReturnsAsync(new AllocationDriftReading(
            true, [new SleeveDriftReading("Bonds", 20m, 32m, 12m, "OverBand")]));

        var act = () => Handler().Handle(
            Command(trigger: RecommendationTriggerKind.IpsBand, triggerRef: "Bonds", sizeUsd: 1_000m), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidRecommendationException>().WithMessage("*not in the Bonds sleeve*");
        _repo.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task A_broken_thesis_does_not_justify_adding_to_the_name()
    {
        var handler = Handler();
        var thesis = new InvestmentThesis { UserId = User, Ticker = "GRAB", BrokenAt = DateTimeOffset.UtcNow, BrokenReason = "margin fell" };
        _theses.Setup(t => t.FindByTickerAsync(User, "GRAB", It.IsAny<CancellationToken>())).ReturnsAsync([thesis]);

        var act = () => handler.Handle(
            Command(RecommendationAction.Add, RecommendationTriggerKind.ThesisBreak, sizeUsd: 500m), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidRecommendationException>().WithMessage("*broken thesis*");
        _repo.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task A_fired_event_about_another_name_cannot_anchor_the_proposal()
    {
        var eventId = Guid.NewGuid();
        _events.Setup(e => e.FindAsync(User, eventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FiredEventReference(eventId, "earnings", "MSFT Q3 results", DateTimeOffset.UtcNow));

        var act = () => Handler().Handle(
            Command(RecommendationAction.Hold, RecommendationTriggerKind.EventVerdict, triggerRef: eventId.ToString()),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidRecommendationException>().WithMessage("*MSFT*not GRAB*");
        _repo.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task An_add_the_risk_policy_refuses_is_clamped_to_the_compliant_size_and_says_so()
    {
        _signals.Setup(s => s.GetRecentAsync("GRAB", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new DossierSignalItem(DateTimeOffset.UtcNow.AddDays(-2), "momentum", "breakout", "info", NoPayload)]);
        var gate = new RiskGateVerdict(RiskGateDecision.Refused, "MaxPositionWeight", 0.14m, 0.095m, 313m);

        var dto = await Handler(gate).Handle(
            Command(RecommendationAction.Add, RecommendationTriggerKind.RadarSignal, sizeUsd: 5_000m), CancellationToken.None);

        dto.SizeShares.Should().Be(100m, "313 / 3.13 rounds down to 100 whole shares");
        dto.OrderText.Should().StartWith("BUY 100 GRAB");
        dto.Evidence.Should().Contain(e => e.Kind == RecommendationEvidenceKind.RiskRule && e.Label.Contains("Sized down"));
    }

    [Fact]
    public async Task An_add_the_risk_policy_refuses_outright_is_not_queued()
    {
        _signals.Setup(s => s.GetRecentAsync("GRAB", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new DossierSignalItem(DateTimeOffset.UtcNow, "momentum", "breakout", "info", NoPayload)]);
        var gate = new RiskGateVerdict(RiskGateDecision.Refused, "MaxPositionWeight", 0.14m, 0.095m, 0m, "over the cap");

        var act = () => Handler(gate).Handle(
            Command(RecommendationAction.Add, RecommendationTriggerKind.RadarSignal, sizeUsd: 5_000m), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidRecommendationException>().WithMessage("*refuses*");
        _repo.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task A_stale_radar_signal_is_not_a_trigger()
    {
        _signals.Setup(s => s.GetRecentAsync("GRAB", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new DossierSignalItem(DateTimeOffset.UtcNow.AddDays(-60), "momentum", "breakout", "info", NoPayload)]);

        var act = () => Handler().Handle(
            Command(RecommendationAction.Add, RecommendationTriggerKind.RadarSignal, sizeUsd: 500m), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidRecommendationException>().WithMessage("*Radar signal*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    public async Task Expiry_outside_one_to_thirty_days_is_refused(int days)
    {
        var act = () => Handler().Handle(Command(expiresInDays: days), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidRecommendationException>();
    }

    [Fact]
    public async Task Evidence_of_an_unknown_kind_is_refused()
    {
        var act = () => Handler().Handle(
            Command(evidence: [new RecommendationEvidence("Vibes", "Feels toppy")]), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidRecommendationException>().WithMessage("*evidence kind*");
    }
}
