namespace FinanceSentry.Modules.Research.API.Responses;

using FinanceSentry.Modules.Research.Domain;

public record ThesisEventDto(
    Guid Id,
    ThesisSubjectType SubjectType,
    Guid SubjectId,
    string Ticker,
    ThesisEventType EventType,
    DateTimeOffset Timestamp,
    decimal? SubjectPrice,
    decimal? BenchmarkPrice,
    string BenchmarkTicker,
    bool PricesPending,
    string? DecisionNote,
    string Origin = "User",
    ThesisAudit? Audit = null);
