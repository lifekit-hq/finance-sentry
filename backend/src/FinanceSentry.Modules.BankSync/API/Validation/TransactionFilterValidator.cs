namespace FinanceSentry.Modules.BankSync.API.Validation;

using FinanceSentry.Core.Api;
using FinanceSentry.Core.Domain;

/// <summary>
/// Validates the transaction filter query parameters shared by both transaction endpoints.
/// Mirrors <c>WealthController</c>'s validation shape: a null-returning check per condition,
/// each carrying the errorCode the frontend registry maps to a message.
/// </summary>
public static class TransactionFilterValidator
{
    public const int MaxSearchLength = 100;

    private static readonly HashSet<string> AllowedTransactionTypes =
        new(StringComparer.OrdinalIgnoreCase) { "debit", "credit" };

    public static ApiErrorBody? ValidateDate(string? raw, out DateOnly? parsed)
    {
        parsed = null;
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        if (!DateOnly.TryParseExact(raw, "yyyy-MM-dd", out var dateOnly))
            return new ApiErrorBody("Date parameters must be in yyyy-MM-dd format.", "INVALID_DATE_RANGE");

        parsed = dateOnly;
        return null;
    }

    public static ApiErrorBody? ValidateDateRange(DateTime? from, DateTime? to)
    {
        if (from.HasValue && to.HasValue && from.Value > to.Value)
            return new ApiErrorBody("Parameter 'from' must be less than or equal to 'to'.", "INVALID_DATE_RANGE");
        return null;
    }

    public static ApiErrorBody? ValidateAmountRange(decimal? min, decimal? max)
    {
        if (min is < 0 || max is < 0)
            return new ApiErrorBody("Amount bounds must not be negative.", "INVALID_AMOUNT_RANGE");
        if (min.HasValue && max.HasValue && min.Value > max.Value)
            return new ApiErrorBody("The minimum amount must be less than or equal to the maximum amount.", "INVALID_AMOUNT_RANGE");
        return null;
    }

    public static ApiErrorBody? ValidateCategories(IReadOnlyList<string>? categories)
    {
        if (categories is null)
            return null;

        foreach (var category in categories)
        {
            if (!CanonicalCategories.Definitions.Any(d => string.Equals(d.Key, category, StringComparison.OrdinalIgnoreCase)))
                return new ApiErrorBody($"Unknown category '{category}'.", "INVALID_CATEGORY");
        }

        return null;
    }

    public static ApiErrorBody? ValidateTransactionType(string? transactionType)
    {
        if (transactionType is not null && !AllowedTransactionTypes.Contains(transactionType))
            return new ApiErrorBody("Parameter 'transactionType' must be 'debit' or 'credit'.", "INVALID_TRANSACTION_TYPE");
        return null;
    }

    public static ApiErrorBody? ValidateSearch(string? search)
    {
        if (search is not null && search.Trim().Length > MaxSearchLength)
            return new ApiErrorBody($"Parameter 'search' must be {MaxSearchLength} characters or fewer.", "INVALID_SEARCH");
        return null;
    }
}
