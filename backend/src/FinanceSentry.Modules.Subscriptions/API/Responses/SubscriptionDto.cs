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
    bool IsTracked);
