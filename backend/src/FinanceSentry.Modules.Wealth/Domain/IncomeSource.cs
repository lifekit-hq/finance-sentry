namespace FinanceSentry.Modules.Wealth.Domain;

using System.Text.Json.Serialization;
using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Domain;
using FinanceSentry.Infrastructure.Auditing;

/// <summary>What the income is: a salary, a side income, or anything else.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<IncomeKind>))]
public enum IncomeKind
{
    Salary,
    Side,
    Other,
}

/// <summary>How often the declared amount arrives. <see cref="Irregular"/> has no monthly equivalent.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<IncomeCadence>))]
public enum IncomeCadence
{
    Monthly,
    Weekly,
    Fortnightly,
    Annual,
    Irregular,
}

/// <summary>Whether the declared amount is before or after tax.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<IncomeBasis>))]
public enum IncomeBasis
{
    Gross,
    Net,
}

/// <summary>
/// Income the person states they receive (#454 S4), so the agent knows what to expect and not only what landed last
/// month. A declared figure is context: it never replaces the observed income in any projection.
/// <para>
/// <see cref="Label"/> and <see cref="Amount"/> are field-encrypted (the column holds ciphertext; the property is
/// plaintext in code) and every save is put on the audit trail by field name. Both are encrypted because a label such
/// as an employer or a client names a person's income source (P2) and the amount is P1; the rest is a shape the person
/// would find in any budget app. Not stored: payslips, a structured employer, tax identifiers.
/// </para>
/// </summary>
[AuditedProfileData(ResourceTypeName)]
public sealed class IncomeSource : IHasUpdatedAt, IUserOwned
{
    /// <summary>The resource type the audit trail records for a write of this entity.</summary>
    public const string ResourceTypeName = "income_source";

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public IncomeKind Kind { get; private set; }

    /// <summary>Encrypted at rest.</summary>
    public string Label { get; private set; } = string.Empty;

    /// <summary>Per <see cref="Cadence"/>, in <see cref="Currency"/>; encrypted at rest.</summary>
    public decimal Amount { get; private set; }

    public string Currency { get; private set; } = string.Empty;
    public IncomeCadence Cadence { get; private set; }
    public IncomeBasis GrossOrNet { get; private set; }
    public DateOnly StartDate { get; private set; }

    /// <summary>Null while the income continues; the last day it is received once it stops.</summary>
    public DateOnly? EndDate { get; private set; }

    /// <summary>When the person retired the source; cleared by an edit, which restates the dates. Null if never retired.</summary>
    public DateTimeOffset? RetiredAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private IncomeSource() { }

    public static IncomeSource Create(
        Guid userId, IncomeKind kind, string label, decimal amount, string currency, IncomeCadence cadence,
        IncomeBasis grossOrNet, DateOnly startDate, DateOnly? endDate)
    {
        var source = new IncomeSource
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        source.Update(kind, label, amount, currency, cadence, grossOrNet, startDate, endDate);
        return source;
    }

    public void Update(
        IncomeKind kind, string label, decimal amount, string currency, IncomeCadence cadence, IncomeBasis grossOrNet,
        DateOnly startDate, DateOnly? endDate)
    {
        Kind = kind;
        Label = label.Trim();
        Amount = amount;
        Currency = currency.ToUpperInvariant();
        Cadence = cadence;
        GrossOrNet = grossOrNet;
        StartDate = startDate;
        EndDate = endDate;
        RetiredAt = null;
    }

    /// <summary>Ends the income today (or on its start date, if it has not started): kept as history, no longer counted.</summary>
    public void Retire(DateOnly today)
    {
        if (EndDate is { } end && end <= today)
            return;

        EndDate = today < StartDate ? StartDate : today;
        RetiredAt = new DateTimeOffset(today.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
    }

    /// <summary>
    /// Whether the income is declared as running on <paramref name="day"/>. A source retired before it started never runs:
    /// its end date is clamped to the start date only so the dates stay valid.
    /// </summary>
    public bool IsActiveOn(DateOnly day) => IsActiveOn(StartDate, EndDate, RetiredAt, day);

    /// <summary>The one rule behind <see cref="IsActiveOn(DateOnly)"/>, shared with the key-less outline read.</summary>
    public static bool IsActiveOn(DateOnly startDate, DateOnly? endDate, DateTimeOffset? retiredAt, DateOnly day) =>
        startDate <= day && (endDate is null || endDate.Value >= day)
        && !(retiredAt is { } retired && DateOnly.FromDateTime(retired.UtcDateTime) < startDate);

    /// <summary>The amount per month, or null when the cadence is irregular (a guess would pass as a fact).</summary>
    public decimal? MonthlyEquivalent => IncomeCadenceMath.MonthlyEquivalent(Amount, Cadence);
}

/// <summary>Cadence arithmetic; a year is 52 weeks (26 fortnights), a month a twelfth of a year.</summary>
public static class IncomeCadenceMath
{
    private const decimal MonthsPerYear = 12m;
    private const decimal WeeksPerYear = 52m;
    private const decimal FortnightsPerYear = 26m;
    private const int Decimals = 2;

    public static decimal? MonthlyEquivalent(decimal amount, IncomeCadence cadence) => cadence switch
    {
        IncomeCadence.Monthly => amount,
        IncomeCadence.Weekly => Math.Round(amount * WeeksPerYear / MonthsPerYear, Decimals),
        IncomeCadence.Fortnightly => Math.Round(amount * FortnightsPerYear / MonthsPerYear, Decimals),
        IncomeCadence.Annual => Math.Round(amount / MonthsPerYear, Decimals),
        _ => null,
    };
}

/// <summary>The bounds an income source is validated against and stored in.</summary>
public static class IncomeSourceLimits
{
    public const int LabelMaxLength = 100;

    /// <summary>Below the 18,2 money ceiling.</summary>
    public const decimal MaxAmount = 1_000_000_000_000m;
}
