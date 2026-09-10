namespace FinanceSentry.Modules.BankSync.Application.Commands;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Infrastructure.Encryption;
using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Domain.Repositories;
using FinanceSentry.Modules.BankSync.Infrastructure.Monobank;
using Hangfire;

public record ConnectMonobankRequest(string Token);

public record ConnectMonobankAccountCommand(Guid UserId, string Token) : ICommand<ConnectMonobankResult>;

public record ConnectedMonobankAccount(
    Guid Id,
    string BankName,
    string AccountType,
    string AccountNumberLast4,
    string OwnerName,
    string Currency,
    decimal? CurrentBalance,
    string SyncStatus,
    string Provider);

public record ConnectMonobankResult(IReadOnlyList<ConnectedMonobankAccount> Accounts);

public class ConnectMonobankAccountCommandHandler(
    IMonobankAdapter monobank,
    ICredentialEncryptionService encryption,
    IBankAccountRepository accounts,
    IMonobankCredentialRepository monobankCredentials,
    IBackgroundJobClient backgroundJobs) : ICommandHandler<ConnectMonobankAccountCommand, ConnectMonobankResult>
{
    public async Task<ConnectMonobankResult> Handle(
        ConnectMonobankAccountCommand request, CancellationToken cancellationToken)
    {
        // Validate token + fetch accounts
        IReadOnlyList<MonobankAccountInfo> monoAccounts;
        try
        {
            monoAccounts = await monobank.ConnectAsync(request.Token, cancellationToken);
        }
        catch (MonobankException ex) when (ex.ErrorCode == "MONOBANK_TOKEN_INVALID")
        {
            throw;
        }

        // Check for duplicate token (same user)
        var existing = await monobankCredentials.GetByUserIdAsync(request.UserId, cancellationToken);
        if (existing != null)
            throw new MonobankException("MONOBANK_TOKEN_DUPLICATE",
                "This Monobank token is already connected.", 409);

        // Encrypt and store credential
        var encrypted = encryption.Encrypt(request.Token);
        var credential = new MonobankCredential(
            userId: request.UserId,
            encryptedToken: encrypted.Ciphertext,
            iv: encrypted.Iv,
            authTag: encrypted.AuthTag,
            keyVersion: encrypted.KeyVersion);
        await monobankCredentials.AddAsync(credential, cancellationToken);

        // Create BankAccount rows
        var created = new List<BankAccount>();
        foreach (var a in monoAccounts)
        {
            var last4 = a.MaskedPan.Length >= 4 ? a.MaskedPan[^4..] : "0000";
            if (!last4.All(char.IsDigit)) last4 = "0000";

            var account = new BankAccount(
                userId: request.UserId,
                externalAccountId: a.Id,
                bankName: "Monobank",
                accountType: a.Type,
                accountNumberLast4: last4,
                ownerName: string.Empty,
                currency: MonobankHttpClient.MapCurrency(a.CurrencyCode),
                createdBy: request.UserId,
                provider: "monobank")
            {
                MonobankCredentialId = credential.Id,
                CurrentBalance = MonobankHttpClient.ToStoredBalance(a.Balance, a.CreditLimit),
                CreditLimit = a.CreditLimit > 0
                    ? MonobankHttpClient.KopecksToDecimal(a.CreditLimit) : null,
                ProductType = a.ProductType
            };

            await accounts.AddAsync(account, cancellationToken);
            created.Add(account);
        }

        // Enqueue 90-day import jobs (T026)
        foreach (var account in created)
            backgroundJobs.Enqueue<FinanceSentry.Modules.BankSync.Infrastructure.Jobs.ScheduledSyncJob>(
                job => job.ExecuteSyncAsync(account.Id));

        var dtos = created.Select(a => new ConnectedMonobankAccount(
            Id: a.Id,
            BankName: a.BankName,
            AccountType: a.AccountType,
            AccountNumberLast4: a.AccountNumberLast4,
            OwnerName: a.OwnerName,
            Currency: a.Currency,
            CurrentBalance: a.CurrentBalance,
            SyncStatus: a.SyncStatus,
            Provider: a.Provider)).ToList();

        return new ConnectMonobankResult(dtos);
    }
}
