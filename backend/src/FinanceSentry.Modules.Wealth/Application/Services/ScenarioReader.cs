namespace FinanceSentry.Modules.Wealth.Application.Services;

using System.Text.Json.Serialization;
using FinanceSentry.Core.Measures;
using FinanceSentry.Core.Utils;
using FinanceSentry.Modules.Wealth.Domain;
using FinanceSentry.Modules.Wealth.Domain.Repositories;

/// <summary>
/// Whether the person's career events shaped a scenario (#454 S5). <see cref="None"/>: no event counts, so no scenario line
/// is shown. <see cref="Applied"/>: the counted events were applied. <see cref="Unreadable"/>: events count but this host
/// holds no encryption key (the MCP host, #613), so their amounts cannot be read and no scenario is claimed. Serialized by name.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ScenarioStatus>))]
public enum ScenarioStatus
{
    None,
    Applied,
    Unreadable,
}

/// <summary>The sentences every scenario carries (money-semantics §19).</summary>
public static class ScenarioText
{
    public const string Label = "A scenario you entered, not a forecast.";

    public const string Basis =
        "Each event changes what you save by its amount from its date; your spending is assumed not to move with it and tax "
        + "is not modelled. Events already in the past are in the baseline's history and are not counted twice. "
        + "The baseline is unchanged.";
}

/// <summary>
/// The counted events in USD, and the day they were counted against. <see cref="Events"/> is empty unless the status is
/// <see cref="ScenarioStatus.Applied"/>.
/// </summary>
public sealed record ScenarioInputs(ScenarioStatus Status, IReadOnlyList<ScenarioEvent> Events, DateOnly Today)
{
    public bool IsApplied => Status == ScenarioStatus.Applied;

    /// <summary>The change to the monthly saving for each of the next <paramref name="months"/> months.</summary>
    public IReadOnlyList<decimal> Deltas(int months) => ScenarioPath.MonthlyDeltas(Events, Today, months);
}

/// <summary>
/// The one reader of a scenario's inputs, so the net-worth, financial-independence and goal projections apply the same
/// events (the way <see cref="ProjectionInputsReader"/> keeps their baseline together). It reads the person's own events with
/// the unscoped variant, keyed on the caller's id, because a projection also runs with no principal (the monthly brief job).
/// On a host that holds no encryption key it reads only the plain columns: it can tell that events count, not what they are.
/// </summary>
public class ScenarioReader(ICareerEventRepository events, TimeProvider clock)
{
    private readonly ICareerEventRepository _events = events;
    private readonly TimeProvider _clock = clock;

    public async Task<ScenarioInputs> ReadAsync(Guid userId, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime);

        if (!_events.CanReadEncryptedFields)
        {
            var outlines = await _events.ListOutlinesUnscopedAsync(userId, ct);
            return new ScenarioInputs(
                outlines.Any(o => o.IsCounted(today)) ? ScenarioStatus.Unreadable : ScenarioStatus.None, [], today);
        }

        var counted = (await _events.ListUnscopedAsync(userId, ct))
            .Where(c => c.IsCounted(today))
            .Select(c => new ScenarioEvent(
                KindOf(c.Kind), c.Date, c.EndDate, Math.Round(CurrencyConverter.ToUsd(c.Amount, c.Currency), MoneyDecimals)))
            .ToList();

        return new ScenarioInputs(counted.Count == 0 ? ScenarioStatus.None : ScenarioStatus.Applied, counted, today);
    }

    private const int MoneyDecimals = 2;

    private static ScenarioEventKind KindOf(CareerEventKind kind) => kind switch
    {
        CareerEventKind.IncomeStep => ScenarioEventKind.IncomeStep,
        CareerEventKind.OneOffIn => ScenarioEventKind.OneOffIn,
        CareerEventKind.OneOffOut => ScenarioEventKind.OneOffOut,
        CareerEventKind.Leave => ScenarioEventKind.Leave,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown career event kind."),
    };
}
