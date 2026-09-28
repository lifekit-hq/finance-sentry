namespace FinanceSentry.Modules.Radar.Application.Services;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Radar.Application.Queries;
using FinanceSentry.Modules.Radar.Domain.MarketStructure;
using FinanceSentry.Modules.Radar.Domain.Ports;

/// <summary><see cref="ISubjectSignalReader"/> impl over Radar's own <see cref="ListSignalsQuery"/>.</summary>
public sealed class SubjectSignalReader(
    IQueryHandler<ListSignalsQuery, IReadOnlyList<RadarSignalDto>> listSignals) : ISubjectSignalReader
{
    public async Task<IReadOnlyList<SubjectSignal>> ListSinceAsync(
        string subject, DateOnly since, CancellationToken ct = default)
    {
        var signals = await listSignals.Handle(new ListSignalsQuery(since, null, null, subject, null), ct);
        return signals
            .Select(s => new SubjectSignal(s.Timestamp, s.Scanner, s.SignalType, s.Severity, s.Payload))
            .ToList();
    }
}
