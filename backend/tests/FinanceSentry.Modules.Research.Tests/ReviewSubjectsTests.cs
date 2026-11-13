namespace FinanceSentry.Modules.Research.Tests;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Opportunity;
using FluentAssertions;
using Xunit;

/// <summary>Which thesis or candidate a hit on a ticker belongs to.</summary>
public sealed class ReviewSubjectsTests
{
    private static InvestmentThesis Thesis(string ticker, params string[] proxies) => new()
    {
        Id = Guid.NewGuid(),
        Ticker = ticker,
        InvalidationTriggers = [.. proxies.Select(p => new ThesisInvalidationTrigger("gross_margin", "lessThan", 0.35m, p))],
    };

    private static OpportunityCandidate Candidate(string ticker) => new() { Id = Guid.NewGuid(), Ticker = ticker, Status = CandidateStatus.Active };

    [Fact]
    public void A_thesis_answers_to_its_own_ticker_and_each_proxy_case_insensitively()
    {
        var thesis = Thesis("SOXX", "MU", "NVDA");
        var subjects = new ReviewSubjects([thesis], []);

        foreach (var ticker in new[] { "SOXX", "soxx", "MU", "nvda" })
        {
            subjects.For(ticker).Should().ContainSingle().Which.Should().Be(
                new ReviewSubject(ReviewSubjectKind.Thesis, thesis.Id, "SOXX"));
        }

        subjects.Tickers.Should().BeEquivalentTo("SOXX", "MU", "NVDA");
    }

    [Fact]
    public void An_Active_candidate_answers_to_its_ticker()
    {
        var candidate = Candidate("CBRS");

        new ReviewSubjects([], [candidate]).For("CBRS").Should().ContainSingle().Which.Should().Be(
            new ReviewSubject(ReviewSubjectKind.Candidate, candidate.Id, "CBRS"));
    }

    [Fact]
    public void A_candidate_on_a_ticker_a_thesis_already_sits_on_is_covered_by_the_thesis()
    {
        var thesis = Thesis("NVDA");

        new ReviewSubjects([thesis], [Candidate("NVDA")]).For("NVDA").Should().ContainSingle()
            .Which.Kind.Should().Be(ReviewSubjectKind.Thesis);
    }

    [Fact]
    public void A_candidate_that_is_only_a_proxy_of_a_thesis_is_still_its_own_subject()
    {
        var thesis = Thesis("SOXX", "MU");
        var candidate = Candidate("MU");

        new ReviewSubjects([thesis], [candidate]).For("MU").Select(s => s.Kind)
            .Should().BeEquivalentTo([ReviewSubjectKind.Thesis, ReviewSubjectKind.Candidate]);
    }

    [Fact]
    public void A_name_with_neither_maps_to_nothing()
        => new ReviewSubjects([Thesis("NVDA")], [Candidate("CBRS")]).For("AAPL").Should().BeEmpty();

    [Fact]
    public void A_thesis_with_a_blank_ticker_or_blank_proxy_adds_no_names()
        => new ReviewSubjects([Thesis(" ", " "), Thesis("NVDA", "")], []).Tickers.Should().BeEquivalentTo("NVDA");
}
