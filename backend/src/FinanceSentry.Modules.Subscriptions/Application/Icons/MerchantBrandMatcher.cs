namespace FinanceSentry.Modules.Subscriptions.Application.Icons;

using System.Text;
using FinanceSentry.Modules.Subscriptions.Domain;

/// <summary>
/// Picks the curated <see cref="MerchantBrand"/> a merchant name belongs to. Names and keywords are
/// split into lower-case alphanumeric tokens and a keyword matches when its tokens appear next to
/// each other, in order, in the name; the most specific keyword (most tokens, then longest) wins.
/// </summary>
public static class MerchantBrandMatcher
{
    public static MerchantBrand? Match(IReadOnlyList<MerchantBrand> brands, string? merchantName)
    {
        if (string.IsNullOrWhiteSpace(merchantName) || brands.Count == 0)
            return null;

        var name = Tokenize(merchantName);
        MerchantBrand? best = null;
        var bestTokens = 0;
        var bestLength = 0;

        foreach (var brand in brands)
        {
            var keyword = Tokenize(brand.Keyword);
            if (keyword.Count == 0 || !ContainsRun(name, keyword))
                continue;

            var length = brand.Keyword.Length;
            if (keyword.Count > bestTokens || (keyword.Count == bestTokens && length > bestLength))
            {
                best = brand;
                bestTokens = keyword.Count;
                bestLength = length;
            }
        }

        return best;
    }

    internal static List<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch))
            {
                current.Append(char.ToLowerInvariant(ch));
            }
            else if (current.Length > 0)
            {
                tokens.Add(current.ToString());
                current.Clear();
            }
        }

        if (current.Length > 0)
            tokens.Add(current.ToString());
        return tokens;
    }

    private static bool ContainsRun(List<string> name, List<string> keyword)
    {
        for (var start = 0; start + keyword.Count <= name.Count; start++)
        {
            var all = true;
            for (var i = 0; i < keyword.Count && all; i++)
                all = string.Equals(name[start + i], keyword[i], StringComparison.Ordinal);
            if (all)
                return true;
        }

        return false;
    }
}
