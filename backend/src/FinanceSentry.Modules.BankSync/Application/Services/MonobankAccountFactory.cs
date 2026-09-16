namespace FinanceSentry.Modules.BankSync.Application.Services;

using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Infrastructure.Monobank;

/// <summary>
/// Builds <see cref="BankAccount"/> rows from a provider-listed Monobank account. The single
/// account-creation path shared by the initial connect flow
/// (<c>ConnectMonobankAccountCommand</c>) and the recurring account-discovery pass, so a new
/// Monobank account is created identically regardless of which caller found it.
/// </summary>
public static class MonobankAccountFactory
{
    public static BankAccount CreateAccount(Guid userId, Guid credentialId, MonobankAccountInfo providerAccount)
    {
        var last4 = providerAccount.MaskedPan.Length >= 4 ? providerAccount.MaskedPan[^4..] : "0000";
        if (!last4.All(char.IsDigit)) last4 = "0000";

        return new BankAccount(
            userId: userId,
            externalAccountId: providerAccount.Id,
            bankName: "Monobank",
            accountType: providerAccount.Type,
            accountNumberLast4: last4,
            ownerName: string.Empty,
            currency: MonobankHttpClient.MapCurrency(providerAccount.CurrencyCode),
            createdBy: userId,
            provider: "monobank")
        {
            MonobankCredentialId = credentialId,
            CurrentBalance = MonobankHttpClient.ToStoredBalance(providerAccount.Balance, providerAccount.CreditLimit),
            CreditLimit = providerAccount.CreditLimit > 0
                ? MonobankHttpClient.KopecksToDecimal(providerAccount.CreditLimit) : null,
            ProductType = providerAccount.ProductType
        };
    }
}
