namespace FinanceSentry.Modules.BankSync.Infrastructure.TrueLayer;

public record TrueLayerProvider(
    string ProviderId,
    string DisplayName,
    string Country,
    string? LogoUrl);

public record TrueLayerTokenSet(
    string AccessToken,
    string RefreshToken,
    int ExpiresInSeconds);

public record TrueLayerAccountInfo(
    string AccountId,
    string DisplayName,
    string Currency,
    string ProviderName,
    string AccountType,
    string? Iban,
    string AccountNumberLast4,
    string? ValidTo = null);

public record TrueLayerAccountBalance(
    decimal Current,
    decimal Available,
    string Currency);

/// <summary>
/// Balance of a credit card from /data/v1/cards/{id}/balance. Unlike an account balance,
/// <see cref="Current"/> is the outstanding amount owed (positive = debt).
/// </summary>
public record TrueLayerCardBalance(
    decimal Current,
    decimal Available,
    decimal? CreditLimit,
    string Currency);

public record TrueLayerTransaction(
    string TransactionId,
    DateTime Timestamp,
    decimal Amount,
    string Currency,
    string Description,
    string? MerchantName,
    string TransactionType,
    bool IsPending,
    IReadOnlyList<string> Classification);
