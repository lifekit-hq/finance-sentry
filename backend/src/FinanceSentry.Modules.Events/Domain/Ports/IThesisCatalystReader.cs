namespace FinanceSentry.Modules.Events.Domain.Ports;

/// <summary>Dated catalysts of the user's unbroken theses (adapter over Research's thesis repository).</summary>
public interface IThesisCatalystReader
{
    Task<IReadOnlyList<ThesisCatalystEntry>> ListActiveAsync(Guid userId, CancellationToken ct = default);
}

public sealed record ThesisCatalystEntry(Guid ThesisId, string Ticker, DateOnly Date, string Event);
