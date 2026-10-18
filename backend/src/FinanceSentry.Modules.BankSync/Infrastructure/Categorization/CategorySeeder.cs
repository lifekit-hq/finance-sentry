namespace FinanceSentry.Modules.BankSync.Infrastructure.Categorization;

using System.Reflection;
using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Infrastructure.Persistence;

/// <summary>
/// Idempotently seeds the reference tables:
///   • <c>categories</c>     — from <see cref="CategorySeedData"/> (Plaid PFC primaries).
///   • <c>mcc_categories</c> — from the embedded greggles/mcc-codes dataset,
///                             category assigned via <see cref="MccRangeClassifier"/>.
///
/// Only missing rows are inserted, so runtime edits to either table are never clobbered.
/// </summary>
public static class CategorySeeder
{
    private const string ResourceSuffix = "Infrastructure.Categorization.Data.mcc_codes.csv";

    public static void Seed(BankSyncDbContext db)
    {
        SeedCategories(db);
        SeedMccCategories(db);
        SeedMerchantKeywords(db);
    }

    private static void SeedMerchantKeywords(BankSyncDbContext db)
    {
        var existing = db.MerchantKeywords.Select(m => m.Keyword).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var toAdd = MerchantKeywordSeedData.Keywords
            .Where(k => !existing.Contains(k.Keyword))
            .Select(k => new MerchantKeyword { Keyword = k.Keyword, CategoryKey = k.CategoryKey })
            .ToList();

        if (toAdd.Count == 0)
            return;

        db.MerchantKeywords.AddRange(toAdd);
        db.SaveChanges();
    }

    private static void SeedCategories(BankSyncDbContext db)
    {
        var existing = db.Categories.Select(c => c.Key).ToHashSet();
        var missing = CategorySeedData.Categories
            .Where(c => !existing.Contains(c.Key))
            .ToList();

        if (missing.Count == 0)
            return;

        db.Categories.AddRange(missing);
        db.SaveChanges();
    }

    private static void SeedMccCategories(BankSyncDbContext db)
    {
        var existing = db.MccCategories.Select(m => m.Mcc).ToHashSet();
        var toAdd = new List<MccCategory>();

        foreach (var (mcc, description) in ReadDataset())
        {
            if (existing.Contains(mcc))
                continue;

            toAdd.Add(new MccCategory
            {
                Mcc = mcc,
                CategoryKey = MccRangeClassifier.Classify(mcc),
                Description = Truncate(description, 255),
            });
            existing.Add(mcc);
        }

        if (toAdd.Count == 0)
            return;

        db.MccCategories.AddRange(toAdd);
        db.SaveChanges();
    }

    private static IEnumerable<(int Mcc, string Description)> ReadDataset()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(ResourceSuffix, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Embedded MCC dataset '{ResourceSuffix}' not found.");

        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);

        // Skip header.
        reader.ReadLine();

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var fields = ParseCsvLine(line);
            if (fields.Count < 2)
                continue;

            if (!int.TryParse(fields[0], out var mcc))
                continue;

            yield return (mcc, fields[1]);
        }
    }

    /// <summary>Minimal RFC-4180 line parser (handles quoted fields with embedded commas).</summary>
    private static List<string> ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    current.Append(c);
                }
            }
            else if (c == '"')
            {
                inQuotes = true;
            }
            else if (c == ',')
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        fields.Add(current.ToString());
        return fields;
    }

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];
}
