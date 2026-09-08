namespace FinanceSentry.Modules.BankSync.API.Responses;

/// <summary>
/// A merchant the user pinned as committed. <paramref name="MerchantKey"/> is the normalized key
/// the policy matches on — surfaced because it is what makes two differently-spelled statement
/// lines the same pin, and callers unpin by it.
/// </summary>
public record CommittedMerchantPinDto(
    Guid Id,
    string MerchantKey,
    string DisplayName,
    DateTime PinnedAt);
