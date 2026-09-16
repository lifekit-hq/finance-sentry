namespace FinanceSentry.Modules.BankSync.Application.Services;

using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Infrastructure.TrueLayer;

/// <summary>
/// Builds <see cref="BankAccount"/> rows from a provider-listed TrueLayer account or card. The
/// single account-creation path shared by the initial connect flow
/// (<c>FinalizeTrueLayerConnectCommand</c>) and the recurring account-discovery pass, so a new
/// TrueLayer account is created identically regardless of which caller found it.
/// </summary>
public static class TrueLayerAccountFactory
{
    public static BankAccount CreateAccount(
        TrueLayerConnection connection, TrueLayerAccountInfo providerAccount, decimal? currentBalance)
        => new(
            userId: connection.UserId,
            externalAccountId: providerAccount.AccountId,
            bankName: !string.IsNullOrWhiteSpace(providerAccount.ProviderName)
                ? providerAccount.ProviderName : connection.ProviderDisplayName,
            accountType: providerAccount.AccountType,
            accountNumberLast4: providerAccount.AccountNumberLast4,
            ownerName: string.Empty,
            currency: providerAccount.Currency,
            createdBy: connection.UserId,
            provider: "truelayer")
        {
            TrueLayerConnectionId = connection.Id,
            CurrentBalance = currentBalance
        };

    public static BankAccount CreateCardAccount(
        TrueLayerConnection connection, TrueLayerAccountInfo providerCard, decimal? owed, decimal? creditLimit)
        => new(
            userId: connection.UserId,
            externalAccountId: providerCard.AccountId,
            bankName: !string.IsNullOrWhiteSpace(providerCard.ProviderName)
                ? providerCard.ProviderName : connection.ProviderDisplayName,
            accountType: "credit",
            accountNumberLast4: providerCard.AccountNumberLast4,
            ownerName: string.Empty,
            currency: providerCard.Currency,
            createdBy: connection.UserId,
            provider: "truelayer")
        {
            TrueLayerConnectionId = connection.Id,
            CurrentBalance = owed,
            CreditLimit = creditLimit,
            ProductType = TrueLayerAdapter.CardProductType
        };
}
