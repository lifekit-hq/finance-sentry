namespace FinanceSentry.Modules.Research.Tests.Persistence;

using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;

/// <summary>
/// Stands in for the real recorder in write-path tests: the production
/// <see cref="ThesisEventRecorder"/> reads <c>thesis_events</c> ordered by a
/// <see cref="DateTimeOffset"/>, which the SQLite provider refuses to translate, so the store
/// behind it cannot be the fixture's. What these tests need from it is only which lifecycle events
/// the handler asked for — and, with <see cref="FailWith"/> set, how the handler behaves when the
/// journal is unavailable (issue #626).
/// </summary>
public sealed class RecordingThesisEventRecorder : IThesisEventRecorder
{
    public List<(Guid SubjectId, ThesisEventType EventType, string? DecisionNote)> Recorded { get; } = [];

    /// <summary>Thrown on every call when set — the journal-is-down case.</summary>
    public Exception? FailWith { get; init; }

    public Task RecordAsync(
        Guid userId,
        ThesisSubjectType subjectType,
        Guid subjectId,
        string ticker,
        ThesisEventType eventType,
        string? decisionNote = null,
        CancellationToken ct = default)
    {
        if (this.FailWith is { } failure)
        {
            return Task.FromException(failure);
        }

        this.Recorded.Add((subjectId, eventType, decisionNote));
        return Task.CompletedTask;
    }
}
