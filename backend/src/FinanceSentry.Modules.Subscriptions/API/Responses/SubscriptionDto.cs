namespace FinanceSentry.Modules.Subscriptions.API.Responses;

public record SubscriptionDto(
    Guid Id,
    string MerchantName,
    string Cadence,
    decimal AverageAmount,
    decimal LastKnownAmount,
    decimal MonthlyEquivalent,
    string Currency,
    DateOnly LastChargeDate,
    DateOnly NextExpectedDate,
    string Status,
    int OccurrenceCount,
    string? Category,
    DateTimeOffset DetectedAt,
    string Kind,
    int? TermCount,
    DateOnly? EndDate,
    DateOnly? StartDate,
    int? RemainingPayments,
    bool IsManual,
    bool IsTracked,
    SubscriptionPriceChangeDto? LatestPriceChange,
    // The curated merchant whose icon GET subscriptions/icons/{IconId} serves; null when no curated
    // merchant matches or its host is known to have no usable icon (the client falls back to a category icon).
    int? IconId = null);

/// <summary>The latest change in what a subscription charges: the previous and the new charge, and when the new one landed.</summary>
public record SubscriptionPriceChangeDto(
    decimal OldAmount,
    decimal NewAmount,
    decimal ChangePercent,
    DateOnly ChargeDate);
