namespace FinanceSentry.Modules.BankSync.Infrastructure.Monobank;

public record MonobankAccountInfo(
    string Id,
    string Name,
    string Type,
    string MaskedPan,
    int CurrencyCode,
    long Balance,
    long CreditLimit,
    string? ProductType = null,
    string? Iban = null,
    string[]? MaskedPans = null);

public record MonobankClientInfo(
    string ClientId,
    string Name,
    IReadOnlyList<MonobankAccountInfo> Accounts);

public record MonobankTransaction(
    string Id,
    long Time,
    string Description,
    int MCC,
    bool Hold,
    long Amount,
    int CurrencyCode,
    long OperationAmount,
    int OperationCurrencyCode,
    long CommissionRate,
    long CashbackAmount,
    long Balance,
    string? Comment,
    string? ReceiptId,
    string? InvoiceId,
    string? CounterName,
    string? CounterIban);
