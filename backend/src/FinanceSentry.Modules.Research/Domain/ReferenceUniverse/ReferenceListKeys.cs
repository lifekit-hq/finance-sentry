namespace FinanceSentry.Modules.Research.Domain.ReferenceUniverse;

/// <summary>The datasets a refresh replaces as a unit; every member row and run row names one.</summary>
public static class ReferenceSources
{
    public const string Sp500 = "sp500";
    public const string Nasdaq100 = "nasdaq100";
    public const string FinanceDatabase = "financedatabase";
    public const string Themes = "themes";

    public static readonly IReadOnlyList<string> All = [Sp500, Nasdaq100, FinanceDatabase, Themes];
}

/// <summary>List-key construction and ticker normalisation shared by the feeds, the refresher and the reader.</summary>
public static class ReferenceListKeys
{
    public const string Sp500 = "sp500";
    public const string Nasdaq100 = "nasdaq100";
    public const string SectorPrefix = "sector:";
    public const string IndustryPrefix = "industry:";
    public const string ThemePrefix = "theme:";

    public static string Sector(string name) => SectorPrefix + name.Trim();

    public static string Industry(string name) => IndustryPrefix + name.Trim();

    public static string Theme(string id) => ThemePrefix + id.Trim().ToLowerInvariant();

    /// <summary>The kind label of a list key: <c>index</c>, <c>sector</c>, <c>industry</c> or <c>theme</c>.</summary>
    public static string KindOf(string listKey) =>
        listKey.StartsWith(SectorPrefix, StringComparison.Ordinal) ? "sector"
        : listKey.StartsWith(IndustryPrefix, StringComparison.Ordinal) ? "industry"
        : listKey.StartsWith(ThemePrefix, StringComparison.Ordinal) ? "theme"
        : "index";

    /// <summary>
    /// Upper-case Yahoo form: a dotted share class (<c>BRK.B</c>) becomes the dash spelling (<c>BRK-B</c>)
    /// the per-ticker Yahoo bar ingestion resolves. Null when nothing usable remains.
    /// </summary>
    public static string? NormalizeTicker(string? raw)
    {
        var t = raw?.Trim().ToUpperInvariant().Replace('.', '-');
        return string.IsNullOrEmpty(t) ? null : t;
    }
}
