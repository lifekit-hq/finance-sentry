namespace FinanceSentry.Modules.BankSync.Application.Services;

using FinanceSentry.Modules.BankSync.Domain.Exceptions;

/// <summary>
/// The one place a caller's merchant text becomes a committed-pin key. Beside the normalizer it
/// delegates to rather than inlined at each call site, so the REST endpoint, the MCP tool and
/// the policy cannot drift into three different normalizations of the same name.
/// </summary>
public static class CommittedMerchantKey
{
    /// <summary>
    /// The key <see cref="CommittedOutflowRules"/> matches a debit against. Derived with the
    /// detector's own <see cref="MerchantNameNormalizer.NormalizeDetectionKey"/> so a pin and a
    /// detected subscription for the same merchant land on the same key.
    /// </summary>
    /// <exception cref="UnpinnableMerchantException">
    /// The text carries no nameable merchant — see the exception.
    /// </exception>
    public static string Derive(string? merchant)
    {
        var key = MerchantNameNormalizer.NormalizeDetectionKey(merchant, description: null);
        if (key == MerchantNameNormalizer.UnknownKey)
            throw new UnpinnableMerchantException();

        return key;
    }
}
