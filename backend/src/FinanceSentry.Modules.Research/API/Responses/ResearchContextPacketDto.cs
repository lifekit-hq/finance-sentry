namespace FinanceSentry.Modules.Research.API.Responses;

public record ResearchContextPacketDto(
    string SubjectType,
    Guid? SubjectId,
    string? Ticker,
    ResearchContextThesisDto? Thesis,
    IReadOnlyList<ResearchContextGroupDto> Groups,
    int OmittedCount,
    DateTimeOffset RetrievedAt);

/// <summary>Null <see cref="ResearchContextPacketDto.Thesis"/> means no thesis context exists for the subject.</summary>
public record ResearchContextThesisDto(
    Guid Id,
    string Ticker,
    string Summary,
    bool IsBroken,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<string>? NarrativeFalsifiers = null);

public record ResearchContextGroupDto(
    string Name,
    IReadOnlyList<ResearchContextItemDto> Items);

public record ResearchContextItemDto(
    Guid DocumentId,
    Guid ChunkId,
    string SourceType,
    string? SourceName,
    string Title,
    string? CanonicalUrl,
    DateTimeOffset? PublishedAt,
    string Snippet,
    double CombinedScore);
