namespace FinanceSentry.Modules.Wealth.Domain;

using System.Text.Json.Serialization;
using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Domain;
using FinanceSentry.Infrastructure.Auditing;

/// <summary>What a dated career-money event does to the monthly saving (#454 S5). Serialized by name.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CareerEventKind>))]
public enum CareerEventKind
{
    /// <summary>Income changes from the date on by a signed monthly amount (a raise is positive, a cut negative).</summary>
    IncomeStep,

    /// <summary>A single amount received on the date (a bonus, a payout).</summary>
    OneOffIn,

    /// <summary>A single amount spent on the date.</summary>
    OneOffOut,

    /// <summary>Income falls by a positive monthly amount from the date through the end date.</summary>
    Leave,
}

/// <summary>
/// A dated life-money event the person enters ("a role change on a date, income steps up by an amount", a bonus, a leave
/// period) so the savings path can be shown with it applied next to the baseline (#454 S5). It is context for a scenario,
/// never a forecast: the baseline (the median of the trailing complete months) stays the default and an event only ever
/// adds a labelled scenario line beside it.
/// <para>
/// <see cref="Label"/>, <see cref="Amount"/> and <see cref="Note"/> are field-encrypted (the columns hold ciphertext; the
/// properties are plaintext in code) and every save is put on the audit trail by field name. A label or note can name an
/// employer or a person (P2) and the amount is P1; the kind, dates and currency are a shape the person would find in any
/// planner. Not stored: payslips, a structured employer, tax identifiers.
/// </para>
/// <para>
/// <see cref="Amount"/> is a signed monthly change for an <see cref="CareerEventKind.IncomeStep"/>, and a positive amount
/// for the other kinds (the monthly reduction of a <see cref="CareerEventKind.Leave"/>, or the one-off itself). Whether
/// the figure is before or after tax is the person's to say in the label; the scenario treats it as the change to what
/// they can save and does not model tax.
/// </para>
/// </summary>
[AuditedProfileData(ResourceTypeName)]
public sealed class CareerEvent : IHasUpdatedAt, IUserOwned
{
    /// <summary>The resource type the audit trail records for a write of this entity.</summary>
    public const string ResourceTypeName = "career_event";

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public CareerEventKind Kind { get; private set; }
    public DateOnly Date { get; private set; }

    /// <summary>The last day of a <see cref="CareerEventKind.Leave"/>; null for every other kind.</summary>
    public DateOnly? EndDate { get; private set; }

    /// <summary>Encrypted at rest.</summary>
    public string Label { get; private set; } = string.Empty;

    /// <summary>In <see cref="Currency"/>; encrypted at rest.</summary>
    public decimal Amount { get; private set; }

    public string Currency { get; private set; } = string.Empty;

    /// <summary>Free text; encrypted at rest. Null when the person wrote none.</summary>
    public string? Note { get; private set; }

    /// <summary>When the person retired the event: kept as history, no longer counted. An edit clears it. Null if never retired.</summary>
    public DateTimeOffset? RetiredAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private CareerEvent() { }

    public static CareerEvent Create(
        Guid userId, CareerEventKind kind, DateOnly date, DateOnly? endDate, string label, decimal amount, string currency, string? note)
    {
        var career = new CareerEvent
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        career.Update(kind, date, endDate, label, amount, currency, note);
        return career;
    }

    public void Update(
        CareerEventKind kind, DateOnly date, DateOnly? endDate, string label, decimal amount, string currency, string? note)
    {
        Kind = kind;
        Date = date;
        EndDate = kind == CareerEventKind.Leave ? endDate : null;
        Label = label.Trim();
        Amount = amount;
        Currency = currency.ToUpperInvariant();
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        RetiredAt = null;
    }

    /// <summary>Takes the event out of the scenario while keeping it as history. Retiring twice keeps the first time.</summary>
    public void Retire(DateTimeOffset now) => RetiredAt ??= now;

    /// <summary>
    /// Whether the event still counts toward a scenario on <paramref name="today"/>: not retired, and not already in the
    /// past (an event that has happened is part of the baseline's history, counting it again would double it). A leave
    /// under way is the exception: it counts until its end date has passed, for the months still ahead.
    /// </summary>
    public bool IsCounted(DateOnly today) => IsCounted(Kind, Date, EndDate, RetiredAt, today);

    /// <summary>The one rule behind <see cref="IsCounted(DateOnly)"/>, shared with the key-less outline read.</summary>
    public static bool IsCounted(CareerEventKind kind, DateOnly date, DateOnly? endDate, DateTimeOffset? retiredAt, DateOnly today) =>
        retiredAt is null && (date > today || (kind == CareerEventKind.Leave && endDate > today));
}

/// <summary>The bounds a career event is validated against and stored in.</summary>
public static class CareerEventLimits
{
    public const int LabelMaxLength = 100;
    public const int NoteMaxLength = 500;

    /// <summary>Below the 18,2 money ceiling.</summary>
    public const decimal MaxAmount = 1_000_000_000_000m;

    /// <summary>How far ahead an event may be dated: a scenario past this is not a plan.</summary>
    public const int MaxYearsAhead = 50;
}
