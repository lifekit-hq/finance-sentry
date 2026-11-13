namespace FinanceSentry.Tests.Unit.Alerts;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Utils;
using FinanceSentry.Modules.Alerts.Application.Services;
using FinanceSentry.Modules.Alerts.Domain;
using FinanceSentry.Modules.Alerts.Domain.Repositories;
using FluentAssertions;
using Moq;
using Xunit;

/// <summary>
/// The thesis- and candidate-review notices: one alert type per subject kind, raised once ever per
/// (subject, source, reference) however often a detector re-reads the same event, and carrying the subject id, source and
/// evidence references on its message.
/// </summary>
public sealed class ReviewDueAlertTests
{
    private readonly Mock<IAlertRepository> _repo = new();
    private readonly Mock<IPolicyAckReader> _acks = new();
    private readonly AlertGeneratorService _service;
    private readonly List<Alert> _ledger = [];
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _subjectId = Guid.NewGuid();

    public ReviewDueAlertTests()
    {
        _service = new AlertGeneratorService(_repo.Object, _acks.Object);
        _repo.Setup(r => r.ExistsAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), default))
            .ReturnsAsync((Guid userId, string type, Guid? referenceId, CancellationToken _) =>
                _ledger.Any(a => a.UserId == userId && a.Type == type && a.ReferenceId == referenceId));
        _repo.Setup(r => r.AddAsync(It.IsAny<Alert>(), default))
            .Callback<Alert, CancellationToken>((a, _) => _ledger.Add(a))
            .Returns(Task.CompletedTask);
    }

    private ReviewDue Review(
        ReviewSubjectKind kind = ReviewSubjectKind.Thesis, string source = ReviewDueSource.Filing, string reference = "0001-26-000123",
        Guid? subjectId = null)
        => new(kind, subjectId ?? _subjectId, "NVDA", source, reference, ["filing:0001-26-000123"], "NVDA: 10-Q filed 2026-11-12");

    [Fact]
    public async Task A_thesis_notice_is_a_ThesisReviewDue_alert_on_the_ticker_carrying_its_payload()
    {
        await _service.GenerateReviewDueAlertAsync(_userId, Review());

        var alert = _ledger.Should().ContainSingle().Subject;
        alert.Type.Should().Be(AlertType.ThesisReviewDue);
        alert.UserId.Should().Be(_userId);
        alert.ReferenceLabel.Should().Be("NVDA");
        alert.Title.Should().Be("Thesis review due: NVDA (filing)");
        alert.Message.Length.Should().BeLessThanOrEqualTo(1000);
        ReviewDueMessage.TryParse(alert.Message, out var payload).Should().BeTrue();
        payload.SubjectId.Should().Be(_subjectId);
        payload.Subject.Should().Be(ReviewSubjectKind.Thesis);
        payload.Source.Should().Be(ReviewDueSource.Filing);
        payload.Reference.Should().Be("0001-26-000123");
        payload.Evidence.Should().Equal("filing:0001-26-000123");
    }

    [Fact]
    public async Task A_candidate_notice_is_a_CandidateReviewDue_alert()
    {
        await _service.GenerateReviewDueAlertAsync(_userId, Review(ReviewSubjectKind.Candidate));

        var alert = _ledger.Should().ContainSingle().Subject;
        alert.Type.Should().Be(AlertType.CandidateReviewDue);
        alert.Title.Should().Be("Candidate review due: NVDA (filing)");
        ReviewDueMessage.TryParse(alert.Message, out var payload).Should().BeTrue();
        payload.Subject.Should().Be(ReviewSubjectKind.Candidate);
    }

    [Fact]
    public async Task The_same_subject_source_and_reference_is_raised_once_however_often_it_is_re_detected()
    {
        for (var i = 0; i < 5; i++)
        {
            await _service.GenerateReviewDueAlertAsync(_userId, Review());
        }

        _ledger.Should().ContainSingle();
    }

    [Fact]
    public async Task Once_means_once_ever_even_after_the_notice_was_dismissed()
    {
        await _service.GenerateReviewDueAlertAsync(_userId, Review());
        _ledger.Single().IsDismissed = true;

        await _service.GenerateReviewDueAlertAsync(_userId, Review());

        _ledger.Should().ContainSingle();
    }

    [Fact]
    public async Task A_different_source_reference_or_subject_is_a_different_notice()
    {
        await _service.GenerateReviewDueAlertAsync(_userId, Review());
        await _service.GenerateReviewDueAlertAsync(_userId, Review(source: ReviewDueSource.News));
        await _service.GenerateReviewDueAlertAsync(_userId, Review(reference: "0001-26-000124"));
        await _service.GenerateReviewDueAlertAsync(_userId, Review(subjectId: Guid.NewGuid()));
        await _service.GenerateReviewDueAlertAsync(_userId, Review(ReviewSubjectKind.Candidate));

        _ledger.Select(a => (a.Type, a.ReferenceId)).Should().OnlyHaveUniqueItems().And.HaveCount(5);
    }

    [Theory]
    [InlineData(ReviewDueSource.Break, AlertSeverity.Warning)]
    [InlineData(ReviewDueSource.Filing, AlertSeverity.Info)]
    [InlineData(ReviewDueSource.Unbreak, AlertSeverity.Info)]
    [InlineData(ReviewDueSource.News, AlertSeverity.Info)]
    public async Task Only_a_break_is_a_warning(string source, string severity)
    {
        await _service.GenerateReviewDueAlertAsync(_userId, Review(source: source, reference: $"ref-{source}"));

        _ledger.Single().Severity.Should().Be(severity);
    }

    [Fact]
    public async Task Every_source_has_a_stable_reference_across_runs()
    {
        foreach (var source in ReviewDueSource.All)
        {
            await _service.GenerateReviewDueAlertAsync(_userId, Review(source: source, reference: "r"));
        }

        var first = _ledger.Select(a => a.ReferenceId).ToList();
        foreach (var source in ReviewDueSource.All)
        {
            await _service.GenerateReviewDueAlertAsync(_userId, Review(source: source, reference: "r"));
        }

        _ledger.Select(a => a.ReferenceId).Should().Equal(first, "a re-read adds nothing");
        first.Should().OnlyHaveUniqueItems();
    }
}
