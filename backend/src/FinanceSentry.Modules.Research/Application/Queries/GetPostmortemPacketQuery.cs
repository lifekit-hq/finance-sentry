namespace FinanceSentry.Modules.Research.Application.Queries;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Repositories;
using Microsoft.Extensions.Options;

public record GetPostmortemPacketQuery(
    Guid UserId, DateOnly PeriodStart, DateOnly PeriodEnd) : IQuery<PostmortemPacketDto>;

/// <summary>
/// FR-008c: compiles every terminal event in a period with its decision notes, entry/exit prices,
/// and excess return, plus rejected-candidate counterfactuals from the same period (SC-004; empty
/// until 019 ships since no Candidate rows exist yet).
/// </summary>
public class GetPostmortemPacketQueryHandler(
    IThesisEventRepository eventRepo,
    IThesisPerformanceCalculator calculator,
    FinanceSentry.Core.Interfaces.IRadarSignalReader signalReader,
    IOptions<FrictionConfig> frictionOptions)
    : IQueryHandler<GetPostmortemPacketQuery, PostmortemPacketDto>
{
    private static readonly ThesisEventType[] TerminalNonCounterfactualTypes =
    [
        ThesisEventType.Closed,
        ThesisEventType.Expired,
        // FR-006/FR-008c: a break is a terminal outcome — a thesis broken during the period
        // belongs in the review packet.
        ThesisEventType.Broken,
        // #1069: a confirmed recommendation is a decision taken; a rejected one is already a counterfactual.
        ThesisEventType.Confirmed,
    ];

    public async Task<PostmortemPacketDto> Handle(GetPostmortemPacketQuery query, CancellationToken ct)
    {
        var periodEvents = await eventRepo.ListForPeriodAsync(
            query.UserId, query.PeriodStart, query.PeriodEnd, ct);

        var terminal = periodEvents
            .Where(e => TerminalNonCounterfactualTypes.Contains(e.EventType))
            .ToList();
        var rejected = periodEvents
            .Where(e => e.EventType == ThesisEventType.Rejected)
            .ToList();

        var entries = new List<PostmortemEntryDto>();
        foreach (var terminalEvent in terminal)
        {
            entries.Add(await BuildEntryAsync(query.UserId, terminalEvent, ct));
        }

        var counterfactuals = new List<PostmortemEntryDto>();
        foreach (var rejectedEvent in rejected)
        {
            counterfactuals.Add(await BuildEntryAsync(query.UserId, rejectedEvent, ct));
        }

        // FR-008c: overrides are decisions too — the review packet must show where the rules were
        // consciously bypassed.
        var overrides = await signalReader.ListOverrideSignalsAsync(
            query.UserId,
            new DateTimeOffset(query.PeriodStart, TimeOnly.MinValue, TimeSpan.Zero),
            new DateTimeOffset(query.PeriodEnd, TimeOnly.MaxValue, TimeSpan.Zero),
            ct);

        return new PostmortemPacketDto(
            query.PeriodStart,
            query.PeriodEnd,
            entries,
            counterfactuals,
            overrides
                .Select(o => new PostmortemOverrideDto(o.Timestamp, o.Scanner, o.SignalType, o.Subject, o.Payload))
                .ToList());
    }

    private async Task<PostmortemEntryDto> BuildEntryAsync(
        Guid userId, ThesisEvent terminalEvent, CancellationToken ct)
    {
        var subjectEvents = await eventRepo.ListAsync(userId, terminalEvent.SubjectId, ct);
        var created = subjectEvents.FirstOrDefault(e => e.EventType == ThesisEventType.Created);

        decimal? excessReturnPct = null;
        if (created is not null)
        {
            var input = new ThesisPerformanceInput(
                terminalEvent.SubjectId,
                ThesisEventType.Created,
                created.Timestamp,
                created.SubjectPrice,
                created.BenchmarkPrice,
                terminalEvent.EventType,
                terminalEvent.Timestamp,
                terminalEvent.SubjectPrice,
                terminalEvent.BenchmarkPrice,
                terminalEvent.Ticker,
                frictionOptions.Value);

            var result = calculator.Calculate(input);
            excessReturnPct = result.ExcessReturnPct;
        }

        return new PostmortemEntryDto(
            terminalEvent.SubjectId,
            terminalEvent.Ticker,
            created?.DecisionNote,
            terminalEvent.DecisionNote,
            created?.SubjectPrice,
            terminalEvent.SubjectPrice,
            excessReturnPct,
            terminalEvent.EventType);
    }
}
