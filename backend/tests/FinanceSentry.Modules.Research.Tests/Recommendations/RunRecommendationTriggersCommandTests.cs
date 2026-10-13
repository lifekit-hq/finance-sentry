namespace FinanceSentry.Modules.Research.Tests.Recommendations;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.Application.Commands;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Recommendations;
using FinanceSentry.Modules.Research.Domain.Repositories;
using FinanceSentry.Modules.Research.Tests.Opportunity;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

public sealed class RunRecommendationTriggersCommandTests
{
    private const decimal Cap = 0.15m;
    private static readonly Guid User = Guid.NewGuid();

    private readonly InMemoryRecommendationRepository _repo = new();
    private readonly JournalRecorder _journal = new();
    private readonly Mock<IThesisRepository> _theses = new();
    private readonly Mock<IPolicyAckReader> _acks = new();
    private List<InvestmentThesis> _userTheses = [];

    /// <summary>The AC7 book: DRAM at about 23% with no thesis, against a 15% single-position cap.</summary>
    private static BookFigures DramBook() => RecommendationTestBook.With(
        100_000m,
        RecommendationTestBook.Stock("DRAM", 1_000m, 23_000m),
        RecommendationTestBook.Stock("VWCE", 500m, 10_000m));

    private RunRecommendationTriggersCommandHandler Handler(BookFigures? book = null, decimal? cap = Cap)
    {
        _theses.Setup(t => t.GetUserIdsWithThesesUnscopedAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _theses.Setup(t => t.ListUnscopedAsync(User, It.IsAny<CancellationToken>())).ReturnsAsync(() => _userTheses);
        var figures = book ?? DramBook();
        return new RunRecommendationTriggersCommandHandler(
            new FakePositionCapSource(cap, [User]),
            _theses.Object,
            _repo,
            _acks.Object,
            new FixedBookFigures(figures),
            RecommendationTestBook.Reader(figures, new Dictionary<string, decimal> { ["DRAM"] = 23m, ["VWCE"] = 20m }),
            new RecommendationWriter(_repo, _journal),
            NullLogger<RunRecommendationTriggersCommandHandler>.Instance);
    }

    [Fact]
    public async Task Ac7_dram_over_the_cap_with_no_thesis_is_queued_as_a_trim_citing_the_missing_thesis()
    {
        var created = await Handler().Handle(new RunRecommendationTriggersCommand(), CancellationToken.None);

        created.Should().Be(1);
        var dram = _repo.Rows.Should().ContainSingle().Subject;
        dram.Ticker.Should().Be("DRAM");
        dram.Action.Should().Be(RecommendationAction.Trim);
        dram.Origin.Should().Be(RecommendationOrigin.Rules);
        dram.Status.Should().Be(RecommendationStatus.Pending);
        dram.SizeShares.Should().Be(348m, "8,000 over a 15% cap at $23 a share rounds up to 348 shares");
        dram.ShareOfBookBeforePct.Should().Be(23.00m);
        dram.ShareOfBookAfterPct.Should().Be(15.00m);
        dram.Evidence.Select(e => e.Kind).Should().Equal(RecommendationEvidenceKind.RiskRule, RecommendationEvidenceKind.Thesis);
        dram.Evidence[0].Label.Should().Be("23.0% of book against a 15% single-position cap");
        dram.Evidence[1].Label.Should().Be("No thesis or exit rule on file");
        _journal.Events.Should().ContainSingle(e => e.SubjectId == dram.Id && e.EventType == ThesisEventType.Created);
    }

    [Fact]
    public async Task A_name_with_a_pending_proposal_is_left_alone()
    {
        _repo.Rows.Add(new Recommendation { UserId = User, Ticker = "DRAM", Action = RecommendationAction.Add, ExpiresAt = DateTimeOffset.UtcNow.AddDays(3) });

        var created = await Handler().Handle(new RunRecommendationTriggersCommand(), CancellationToken.None);

        created.Should().Be(0);
    }

    [Fact]
    public async Task A_rejected_cap_trim_is_not_re_proposed_inside_the_cooldown()
    {
        _repo.Rows.Add(new Recommendation
        {
            UserId = User, Ticker = "DRAM", Status = RecommendationStatus.Rejected,
            TriggerKind = RecommendationTriggerKind.RiskRule, TriggerRef = RecommendationEvidenceFactory.MaxPositionWeightRule,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-5),
        });

        var created = await Handler().Handle(new RunRecommendationTriggersCommand(), CancellationToken.None);

        created.Should().Be(0);
    }

    [Fact]
    public async Task An_acknowledged_cap_breach_stays_silent()
    {
        _acks.Setup(a => a.IsPolicySilencedAsync(User, RecommendationEvidenceFactory.MaxPositionWeightRule, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var created = await Handler().Handle(new RunRecommendationTriggersCommand(), CancellationToken.None);

        created.Should().Be(0);
    }

    [Fact]
    public async Task Without_a_cap_nothing_is_over_it()
        => (await Handler(cap: null).Handle(new RunRecommendationTriggersCommand(), CancellationToken.None)).Should().Be(0);

    [Fact]
    public async Task A_broken_thesis_on_a_held_name_is_queued_as_an_exit_once_per_break()
    {
        var thesis = new InvestmentThesis { UserId = User, Ticker = "VWCE", BrokenAt = DateTimeOffset.UtcNow.AddHours(-2), BrokenReason = "fees rose" };
        _userTheses = [thesis];
        var handler = Handler();

        await handler.Handle(new RunRecommendationTriggersCommand(), CancellationToken.None);

        var exit = _repo.Rows.Single(r => r.Ticker == "VWCE");
        exit.Action.Should().Be(RecommendationAction.Exit);
        exit.TriggerKind.Should().Be(RecommendationTriggerKind.ThesisBreak);
        exit.TriggerRef.Should().Be(thesis.Id.ToString());
        exit.SizeShares.Should().Be(500m);
        exit.Rationale.Should().Contain("fees rose");

        exit.Status = RecommendationStatus.Rejected;
        await handler.Handle(new RunRecommendationTriggersCommand(), CancellationToken.None);
        _repo.Rows.Count(r => r.Ticker == "VWCE").Should().Be(1, "the same break is not proposed twice");
    }

    [Fact]
    public async Task A_name_over_the_cap_with_a_thesis_cites_the_thesis_instead()
    {
        var thesis = new InvestmentThesis { UserId = User, Ticker = "DRAM" };
        _userTheses = [thesis];

        await Handler().Handle(new RunRecommendationTriggersCommand(), CancellationToken.None);

        _repo.Rows.Single().Evidence[1].RefId.Should().Be(thesis.Id.ToString());
    }

    [Fact]
    public async Task An_empty_book_proposes_nothing()
        => (await Handler(RecommendationTestBook.With(0m)).Handle(new RunRecommendationTriggersCommand(), CancellationToken.None))
            .Should().Be(0);
}
