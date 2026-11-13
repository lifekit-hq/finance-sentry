namespace FinanceSentry.Modules.Research.Tests.Screens;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.Application.Commands;
using FinanceSentry.Modules.Research.Application.Services.Coverage;
using FinanceSentry.Modules.Research.Application.Services.Screens;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Coverage;
using FinanceSentry.Modules.Research.Domain.Opportunity;
using FinanceSentry.Modules.Research.Domain.Repositories;
using FinanceSentry.Modules.Research.Domain.Scoring;
using FinanceSentry.Modules.Research.Domain.Screens;
using FinanceSentry.Modules.Research.Infrastructure.Persistence.Repositories;
using FinanceSentry.Modules.Research.Tests.Coverage;
using FinanceSentry.Modules.Research.Tests.Opportunity;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

/// <summary>
/// The nominator over a real in-memory research store: what a screen's hit becomes (a tagged Scan candidate), and the
/// guards between a hit and a nomination (caps, coverage, the removal cooldown, idempotency). Screens are scripted.
/// </summary>
public sealed class ScreenNominatorTests
{
    private static readonly DateTimeOffset AsOf = CoverageHarness.Day1.AddHours(22);

    private sealed class ScriptedScreen(string tag, int cap, params ScreenHit[] hits) : IScreen
    {
        public string Tag => tag;

        public bool Enabled { get; set; } = true;

        public int MaxNominationsPerRun => cap;

        public Exception? Failure { get; set; }

        public int Runs { get; private set; }

        public Task<ScreenRun> RunAsync(DateTimeOffset asOf, CancellationToken ct = default)
        {
            Runs++;
            return Failure is null ? Task.FromResult(new ScreenRun(tag, hits, hits.Length, null)) : Task.FromException<ScreenRun>(Failure);
        }
    }

    /// <summary>The real upsert and reason bookkeeping of the score command, without its provider reads.</summary>
    private sealed class UpsertingScorer(CoverageHarness h) : ICommandHandler<ScoreCandidateCommand, ScoreCandidateResult>
    {
        public List<ScoreCandidateCommand> Commands { get; } = [];

        public HashSet<string> Failing { get; } = new(StringComparer.OrdinalIgnoreCase);

        public async Task<ScoreCandidateResult> Handle(ScoreCandidateCommand command, CancellationToken ct)
        {
            Commands.Add(command);
            if (Failing.Contains(command.Ticker))
            {
                throw new InvalidOperationException("scoring failed");
            }

            var repo = new CandidateRepository(h.Db);
            var (candidate, isNew) = await repo.UpsertActiveAsync(command.UserId, command.Ticker, command.Source, TimeSpan.FromDays(30), ct);
            candidate.NominationReasons.AddRange(
                (command.NominationReasons ?? []).Where(r => !candidate.NominationReasons.Contains(r, StringComparer.OrdinalIgnoreCase)));
            await repo.UpdateAsync(candidate, ct);
            h.Db.CandidateScores.Add(new CandidateScore
            {
                CandidateId = candidate.Id,
                ScoredAt = CoverageHarness.Day1.AddDays(-1),
                StructureScore = 70,
                FundamentalsScore = 70,
            });
            await h.Db.SaveChangesAsync(ct);
            return new ScoreCandidateResult(
                candidate.Id, command.Ticker, isNew,
                new CandidateScorecard(70, 70, CrowdingClass.Normal, IpsFitFacts.Unknown, ScoreEvidence.Empty, 1));
        }
    }

    private sealed class Rig
    {
        public Rig(ScreenOptions? options = null, CoverageOptions? coverage = null, params IScreen[] screens)
        {
            Options = options ?? new ScreenOptions();
            Harness = new CoverageHarness(coverage);
            Scorer = new UpsertingScorer(Harness);
            Screens = screens;
            Ips = new FakeIpsRepository(new InvestmentPolicyStatement { UserId = CoverageHarness.UserId });
        }

        public ScreenOptions Options { get; }

        public CoverageHarness Harness { get; }

        public UpsertingScorer Scorer { get; }

        public IScreen[] Screens { get; }

        public IIpsRepository Ips { get; set; }

        public ScreenNominator Nominator => new(
            Screens, Harness.Coverage, new CandidateRepository(Harness.Db), Ips, Scorer,
            Microsoft.Extensions.Options.Options.Create(Options), Microsoft.Extensions.Options.Options.Create(Harness.Options),
            NullLogger<ScreenNominator>.Instance);

        public Task<ScreenRunSummary> RunAsync(DateTimeOffset? at = null) => Nominator.RunAsync(at ?? AsOf);

        public Task<List<OpportunityCandidate>> CandidatesAsync()
            => Harness.Db.OpportunityCandidates.IgnoreQueryFilters().AsNoTracking().OrderBy(c => c.Ticker).ToListAsync();
    }

    private static ScreenHit Hit(string tag, string ticker, DateTimeOffset? evidence = null, params string[] supporting)
        => new(tag, ticker, supporting, $"detail for {ticker}", evidence ?? AsOf.AddDays(-1));

    // ── what a hit becomes ────────────────────────────────────────────────────

    [Fact]
    public async Task AHit_BecomesAScanCandidate_WhoseFirstReasonIsTheScreensTag()
    {
        var rig = new Rig(screens: new ScriptedScreen(ScreenTags.RelativeStrength, 3, Hit(ScreenTags.RelativeStrength, "MU", null, "scan: top-decile RS")));

        var summary = await rig.RunAsync();

        summary.Nominated.Should().Be(1);
        var command = rig.Scorer.Commands.Should().ContainSingle().Which;
        command.Source.Should().Be(CandidateSource.Scan);
        command.UserId.Should().Be(CoverageHarness.UserId);
        command.NominationReasons.Should().Equal(ScreenTags.RelativeStrength, "scan: top-decile RS");
        command.DecisionNote.Should().Be($"{ScreenTags.RelativeStrength}: detail for MU");
        var candidate = (await rig.CandidatesAsync()).Single();
        candidate.NominationReasons[0].Should().Be(ScreenTags.RelativeStrength, "the Ledger scoreboard attributes a coverage add to the first reason");
    }

    [Fact]
    public async Task EachScreensNominationCarriesItsOwnTag()
    {
        var rig = new Rig(screens: [
            new ScriptedScreen(ScreenTags.Fundamentals, 3, Hit(ScreenTags.Fundamentals, "AAA")),
            new ScriptedScreen(ScreenTags.Catalyst, 3, Hit(ScreenTags.Catalyst, "BBB")),
            new ScriptedScreen(ScreenTags.RelativeStrength, 3, Hit(ScreenTags.RelativeStrength, "CCC"))]);

        await rig.RunAsync();

        (await rig.CandidatesAsync()).ToDictionary(c => c.Ticker, c => c.NominationReasons[0]).Should().Equal(
            new Dictionary<string, string>
            {
                ["AAA"] = ScreenTags.Fundamentals,
                ["BBB"] = ScreenTags.Catalyst,
                ["CCC"] = ScreenTags.RelativeStrength,
            });
    }

    [Fact]
    public async Task ANominationThatPassesStage1_JoinsCoverageAsAScreenAdd_AttributedToTheScreen()
    {
        var rig = new Rig(screens: new ScriptedScreen(ScreenTags.Fundamentals, 3, Hit(ScreenTags.Fundamentals, "MU")));
        await rig.RunAsync();
        rig.Harness.Verdicts.Verdicts["MU"] = "Buy";

        await rig.Harness.RunAsync(CoverageHarness.Day1);
        await rig.Harness.RunAsync(CoverageHarness.Day1.AddDays(1));

        var member = (await rig.Harness.MembersAsync()).Should().ContainSingle().Which;
        member.Ticker.Should().Be("MU");
        member.AddedReason.Should().Be(CoverageReason.Screen);
        (await rig.Harness.JournalAsync()).Single().Rule.Should().Be(CoverageRuleIds.BuyTwice);
    }

    // ── caps ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task APerScreenCap_KeepsTheBestHits_AndSkippedNamesDoNotUseItUp()
    {
        var rig = new Rig(screens: new ScriptedScreen(
            ScreenTags.Fundamentals, 2,
            Hit(ScreenTags.Fundamentals, "AAA"), Hit(ScreenTags.Fundamentals, "BBB"),
            Hit(ScreenTags.Fundamentals, "CCC"), Hit(ScreenTags.Fundamentals, "DDD")));
        await rig.Harness.MemberAsync("AAA");

        var summary = await rig.RunAsync();

        summary.Nominated.Should().Be(2);
        summary.SkippedCovered.Should().Be(1);
        rig.Scorer.Commands.Select(c => c.Ticker).Should().Equal("BBB", "CCC");
    }

    [Fact]
    public async Task APerUserCap_BoundsAllScreensTogether()
    {
        var options = new ScreenOptions { MaxNominationsPerUserPerRun = 3 };
        var rig = new Rig(options, null,
            new ScriptedScreen(ScreenTags.Fundamentals, 5, Hit(ScreenTags.Fundamentals, "A1"), Hit(ScreenTags.Fundamentals, "A2")),
            new ScriptedScreen(ScreenTags.Catalyst, 5, Hit(ScreenTags.Catalyst, "B1"), Hit(ScreenTags.Catalyst, "B2")));

        var summary = await rig.RunAsync();

        summary.Nominated.Should().Be(3);
        rig.Scorer.Commands.Select(c => c.Ticker).Should().Equal("A1", "A2", "B1");
    }

    [Fact]
    public async Task TheCoverageCap_LimitsNominationsToTheRoomLeft_AndAFullListNominatesNothing()
    {
        var coverage = new CoverageOptions { MaxMembers = 3 };
        var screen = new ScriptedScreen(ScreenTags.Fundamentals, 5, Hit(ScreenTags.Fundamentals, "A1"), Hit(ScreenTags.Fundamentals, "A2"), Hit(ScreenTags.Fundamentals, "A3"));
        var rig = new Rig(null, coverage, screen);
        await rig.Harness.MemberAsync("OLD1");
        await rig.Harness.MemberAsync("OLD2");

        (await rig.RunAsync()).Nominated.Should().Be(1, "one slot is left under the cap of 3");

        await rig.Harness.MemberAsync("OLD3");
        rig.Scorer.Commands.Clear();
        (await rig.RunAsync()).Nominated.Should().Be(0);
        rig.Scorer.Commands.Should().BeEmpty();
    }

    // ── what is not nominated ─────────────────────────────────────────────────

    [Fact]
    public async Task ACoveredName_IsNotNominated()
    {
        var rig = new Rig(screens: new ScriptedScreen(ScreenTags.Catalyst, 3, Hit(ScreenTags.Catalyst, "MU")));
        await rig.Harness.MemberAsync("MU");

        var summary = await rig.RunAsync();

        summary.Nominated.Should().Be(0);
        summary.SkippedCovered.Should().Be(1);
        rig.Scorer.Commands.Should().BeEmpty();
    }

    [Fact]
    public async Task ARerun_ChangesNothing_NoSecondScoreNoSecondCandidateNoDuplicateReason()
    {
        var rig = new Rig(screens: new ScriptedScreen(ScreenTags.RelativeStrength, 3, Hit(ScreenTags.RelativeStrength, "MU", null, "scan: top-decile RS")));

        var first = await rig.RunAsync();
        var second = await rig.RunAsync(AsOf.AddHours(1));

        first.Nominated.Should().Be(1);
        second.Nominated.Should().Be(0);
        second.SkippedAlreadyNominated.Should().Be(1);
        rig.Scorer.Commands.Should().HaveCount(1);
        var candidate = (await rig.CandidatesAsync()).Should().ContainSingle().Which;
        candidate.NominationReasons.Should().Equal(ScreenTags.RelativeStrength, "scan: top-decile RS");
        (await rig.Harness.Db.CandidateScores.IgnoreQueryFilters().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task AnotherScreenFindingTheSameName_AddsItsOwnReason_WithoutTakingOverTheLeadingTag()
    {
        var rig = new Rig(screens: [
            new ScriptedScreen(ScreenTags.Fundamentals, 3, Hit(ScreenTags.Fundamentals, "MU")),
            new ScriptedScreen(ScreenTags.Catalyst, 3, Hit(ScreenTags.Catalyst, "MU"))]);

        var summary = await rig.RunAsync();

        summary.Nominated.Should().Be(2, "a second screen's evidence is worth a second scoring");
        (await rig.CandidatesAsync()).Single().NominationReasons.Should().Equal(ScreenTags.Fundamentals, ScreenTags.Catalyst);
        (await rig.RunAsync(AsOf.AddHours(2))).Nominated.Should().Be(0);
    }

    // ── removed names ─────────────────────────────────────────────────────────

    private static async Task RemovedAsync(Rig rig, string ticker, DateTimeOffset at)
    {
        rig.Harness.Db.CoverageJournal.Add(new CoverageJournalEntry
        {
            UserId = CoverageHarness.UserId,
            Ticker = ticker,
            Action = CoverageAction.Removed,
            Rule = CoverageRuleIds.AvoidTwice,
            Reason = CoverageReason.Screen,
            At = at,
        });
        await rig.Harness.Db.SaveChangesAsync();
    }

    [Fact]
    public async Task ARemovedName_IsNotRenominated_OnEvidenceThatPredatesTheRemoval()
    {
        var removed = AsOf.AddDays(-40);
        var rig = new Rig(screens: new ScriptedScreen(ScreenTags.Fundamentals, 3, Hit(ScreenTags.Fundamentals, "MU", removed.AddDays(-5))));
        await RemovedAsync(rig, "MU", removed);

        var summary = await rig.RunAsync();

        summary.Nominated.Should().Be(0);
        summary.SkippedSuppressed.Should().Be(1);
        (await rig.CandidatesAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task ARemovedName_IsNotRenominated_WithinTheCooldown_EvenOnNewerEvidence()
    {
        var removed = AsOf.AddDays(-10);
        var rig = new Rig(screens: new ScriptedScreen(ScreenTags.Catalyst, 3, Hit(ScreenTags.Catalyst, "MU", removed.AddDays(1))));
        await RemovedAsync(rig, "MU", removed);

        (await rig.RunAsync()).SkippedSuppressed.Should().Be(1);
        (await rig.CandidatesAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task ARemovedName_IsRenominated_OnceEvidenceIsNewerAndTheCooldownHasPassed()
    {
        var removed = AsOf.AddDays(-40);
        var rig = new Rig(screens: new ScriptedScreen(ScreenTags.Fundamentals, 3, Hit(ScreenTags.Fundamentals, "MU", removed.AddDays(5))));
        await RemovedAsync(rig, "MU", removed);

        (await rig.RunAsync()).Nominated.Should().Be(1);
    }

    [Fact]
    public async Task ARemovalOfOneName_DoesNotHoldBackAnother()
    {
        var removed = AsOf.AddDays(-5);
        var rig = new Rig(screens: new ScriptedScreen(ScreenTags.Fundamentals, 3, Hit(ScreenTags.Fundamentals, "MU"), Hit(ScreenTags.Fundamentals, "WDC")));
        await RemovedAsync(rig, "MU", removed);

        var summary = await rig.RunAsync();

        summary.SkippedSuppressed.Should().Be(1);
        rig.Scorer.Commands.Select(c => c.Ticker).Should().Equal("WDC");
    }

    // ── failure and switches ──────────────────────────────────────────────────

    [Fact]
    public async Task AScreenThatFails_DoesNotStopTheOthers_ButTheRunEndsFailed()
    {
        var broken = new ScriptedScreen(ScreenTags.Catalyst, 3) { Failure = new InvalidOperationException("calendar down") };
        var rig = new Rig(screens: [broken, new ScriptedScreen(ScreenTags.Fundamentals, 3, Hit(ScreenTags.Fundamentals, "MU"))]);

        var act = () => rig.RunAsync();

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*1 screen(s) failed*");
        rig.Scorer.Commands.Select(c => c.Ticker).Should().Equal("MU");
    }

    [Fact]
    public async Task ANameThatCannotBeScored_IsCounted_AndTheRestStillGoThrough()
    {
        var rig = new Rig(screens: new ScriptedScreen(ScreenTags.Fundamentals, 3, Hit(ScreenTags.Fundamentals, "BAD"), Hit(ScreenTags.Fundamentals, "GOOD")));
        rig.Scorer.Failing.Add("BAD");

        var summary = await rig.RunAsync();

        summary.Errors.Should().Be(1);
        summary.Nominated.Should().Be(1);
        (await rig.CandidatesAsync()).Select(c => c.Ticker).Should().Equal("GOOD");
    }

    [Fact]
    public async Task WithNoUserToNominateFor_NoScreenIsRun()
    {
        var screen = new ScriptedScreen(ScreenTags.Fundamentals, 3, Hit(ScreenTags.Fundamentals, "MU"));
        var rig = new Rig(screens: screen) { Ips = new FakeIpsRepository() };

        var summary = await rig.RunAsync();

        summary.Should().Be(ScreenRunSummary.Empty);
        screen.Runs.Should().Be(0, "no EDGAR reads are spent when nobody can receive a nomination");
    }

    [Fact]
    public async Task ADisabledRunOrScreen_DoesNothing()
    {
        var off = new ScriptedScreen(ScreenTags.Fundamentals, 3, Hit(ScreenTags.Fundamentals, "AAA")) { Enabled = false };
        var on = new ScriptedScreen(ScreenTags.Catalyst, 3, Hit(ScreenTags.Catalyst, "BBB"));
        var rig = new Rig(screens: [off, on]);

        await rig.RunAsync();
        rig.Scorer.Commands.Select(c => c.Ticker).Should().Equal("BBB");
        off.Runs.Should().Be(0);

        var disabled = new Rig(new ScreenOptions { Enabled = false }, null, on);
        (await disabled.RunAsync()).Should().Be(ScreenRunSummary.Empty);
        disabled.Scorer.Commands.Should().BeEmpty();
    }
}
