namespace FinanceSentry.Modules.Research.Application.Commands;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Validation;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Exceptions;
using FinanceSentry.Modules.Research.Domain.Opportunity;
using FinanceSentry.Modules.Research.Domain.Repositories;
using FinanceSentry.Modules.Research.Domain.Revisions;

/// <summary>
/// Puts one change to a thesis (or one promotion) to the owner as a pending artifact. Nothing is written to the
/// thesis, the candidate or the book: the owner's confirm is the single step that does that
/// (<see cref="DecideThesisRevisionCommand"/>). A newer proposal on the same subject supersedes the open one. Content is
/// validated now against the same rules a direct save applies, so a proposal the owner could never confirm is refused
/// at the door rather than failing on confirm.
/// </summary>
public sealed record ProposeThesisRevisionCommand(
    Guid UserId,
    ThesisRevisionKind Kind,
    Guid SubjectId,
    string Rationale,
    ThesisRevisionContent Content,
    ThesisEvidenceRefs? Evidence = null,
    decimal ProposedUsd = 0m,
    bool Paper = false,
    int? ExpiresInDays = null,
    ThesisEventOrigin ProposedBy = ThesisEventOrigin.Ledger) : ICommand<ThesisRevisionDto>;

public sealed class ProposeThesisRevisionCommandHandler(
    IThesisRepository theses,
    ICandidateRepository candidates,
    IThesisRevisionRepository revisions)
    : ICommandHandler<ProposeThesisRevisionCommand, ThesisRevisionDto>
{
    private const int MaxThesisTextLength = 4000;

    public async Task<ThesisRevisionDto> Handle(ProposeThesisRevisionCommand command, CancellationToken ct)
    {
        var rationale = command.Rationale?.Trim();
        if (string.IsNullOrEmpty(rationale))
        {
            throw new InvalidThesisRevisionException("a rationale is required - the owner decides on it.");
        }

        if (rationale.Length > ThesisRevision.MaxRationaleLength)
        {
            throw new InvalidThesisRevisionException($"the rationale is at most {ThesisRevision.MaxRationaleLength} characters.");
        }

        if (command.ProposedUsd < 0)
        {
            throw new InvalidThesisRevisionException("proposedUsd cannot be negative.");
        }

        var content = Normalize(command.Content);
        var (subjectType, ticker) = await ValidateSubjectAsync(command, content, ct);

        var now = DateTimeOffset.UtcNow;
        var days = Math.Clamp(command.ExpiresInDays ?? ThesisRevision.DefaultExpiryDays, 1, ThesisRevision.MaxExpiryDays);
        var revision = new ThesisRevision
        {
            UserId = command.UserId,
            Kind = command.Kind,
            ProposedBy = command.ProposedBy,
            SubjectType = subjectType,
            SubjectId = command.SubjectId,
            Ticker = ticker,
            Proposed = content,
            Rationale = rationale,
            Evidence = command.Evidence,
            ProposedUsd = command.Kind == ThesisRevisionKind.Promote ? command.ProposedUsd : 0m,
            Paper = command.Kind == ThesisRevisionKind.Promote && command.Paper,
            CreatedAt = now,
            ExpiresAt = now.AddDays(days),
        };

        var open = await revisions.FindPendingAsync(command.UserId, command.SubjectId, ct);
        if (open is not null)
        {
            if (open.ExpiresAt <= now)
            {
                open.Status = ThesisRevisionStatus.Expired;
            }
            else
            {
                open.Status = ThesisRevisionStatus.Superseded;
                open.DecidedAt = now;
                open.SupersededById = revision.Id;
            }
        }

        await revisions.AddPendingAsync(revision, open, ct);
        return ThesisRevisionDto.From(revision);
    }

    private static ThesisRevisionContent Normalize(ThesisRevisionContent content)
    {
        if (content.ThesisText is { } text && string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidThesisRevisionException("thesisText cannot be blank; leave it out to keep the current text.");
        }

        if (content.ThesisText is { Length: > MaxThesisTextLength })
        {
            throw new InvalidThesisRevisionException($"thesisText is at most {MaxThesisTextLength} characters.");
        }

        if (content.InvalidationTriggers is { } triggers)
        {
            ThesisTriggerVocabulary.Validate(triggers);
        }

        return content with
        {
            ThesisText = content.ThesisText?.Trim(),
            NarrativeFalsifiers = content.NarrativeFalsifiers is null
                ? null
                : NarrativeFalsifierRules.Normalize(content.NarrativeFalsifiers),
        };
    }

    private async Task<(ThesisSubjectType SubjectType, string Ticker)> ValidateSubjectAsync(
        ProposeThesisRevisionCommand command, ThesisRevisionContent content, CancellationToken ct)
    {
        if (command.Kind == ThesisRevisionKind.Promote)
        {
            var candidate = await candidates.GetAsync(command.UserId, command.SubjectId, ct);
            if (candidate is null || candidate.Status != CandidateStatus.Active)
            {
                throw new InvalidThesisRevisionException("the candidate does not exist or is no longer active.");
            }

            return (ThesisSubjectType.Candidate, candidate.Ticker);
        }

        var thesis = await theses.FindAsync(command.UserId, command.SubjectId, ct)
            ?? throw new ThesisNotFoundException();

        switch (command.Kind)
        {
            case ThesisRevisionKind.Close:
                if (!content.IsEmpty)
                {
                    throw new InvalidThesisRevisionException("a Close proposal carries no content, only a rationale.");
                }

                break;
            case ThesisRevisionKind.NarrativeUpdate:
                if (content.InvalidationTriggers is not null)
                {
                    throw new InvalidThesisRevisionException("a NarrativeUpdate does not change triggers; propose a TriggerChange.");
                }

                RequireChange(thesis, content);
                break;
            case ThesisRevisionKind.TriggerChange:
                if (content.InvalidationTriggers is null)
                {
                    throw new InvalidThesisRevisionException("a TriggerChange carries the invalidation triggers it proposes.");
                }

                if (content.ThesisText is not null || content.KeyDataPoints is not null || content.Catalysts is not null
                    || content.NarrativeFalsifiers is not null || content.EntryPrice is not null)
                {
                    throw new InvalidThesisRevisionException("a TriggerChange changes triggers only; propose a NarrativeUpdate for the rest.");
                }

                RequireChange(thesis, content);
                break;
        }

        return (ThesisSubjectType.Thesis, thesis.Ticker);
    }

    private static void RequireChange(InvestmentThesis thesis, ThesisRevisionContent content)
    {
        var current = ThesisVersion.Of(thesis);
        if (current.SameContentAs(ThesisRevisionMerge.Apply(current, content)))
        {
            throw new InvalidThesisRevisionException("the proposal changes nothing in the thesis as it stands.");
        }
    }
}
