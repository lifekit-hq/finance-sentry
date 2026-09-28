namespace FinanceSentry.Modules.Research.Domain.Ports;

/// <summary>
/// Published read port (#673): the dated catalysts of a user's unbroken theses, for cross-module
/// readers such as the Events module. Implemented inside Research; the FinanceSentry.Integration
/// adapter reaches Research only through this interface.
/// </summary>
public interface IActiveThesisCatalystReader
{
    /// <summary>Every catalyst on every thesis of the user that is not broken, regardless of date.</summary>
    Task<IReadOnlyList<ActiveThesisCatalyst>> ListAsync(Guid userId, CancellationToken ct = default);
}

/// <summary>One dated catalyst on an unbroken thesis.</summary>
public sealed record ActiveThesisCatalyst(
    Guid ThesisId,
    string Ticker,
    DateOnly Date,
    string Event);
