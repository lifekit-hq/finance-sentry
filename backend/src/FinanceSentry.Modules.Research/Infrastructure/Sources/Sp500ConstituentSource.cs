namespace FinanceSentry.Modules.Research.Infrastructure.Sources;

using System.Reflection;
using System.Text.Json;
using FinanceSentry.Core.Interfaces;

/// <summary>
/// Reads the checked-in S&amp;P 500 constituent list from the embedded resource. Registered as a
/// singleton; the parsed list is cached for the process lifetime (the file only changes on deploy).
/// </summary>
public sealed class Sp500ConstituentSource : IIndexConstituentSource
{
    private const string ResourceName =
        "FinanceSentry.Modules.Research.Infrastructure.Resources.sp500-constituents.json";

    private readonly Lazy<IReadOnlyList<string>> constituents = new(Load, isThreadSafe: true);

    public IReadOnlyList<string> GetConstituents() => constituents.Value;

    private static IReadOnlyList<string> Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded seed resource '{ResourceName}' not found.");
        using var reader = new StreamReader(stream);

        var doc = JsonSerializer.Deserialize<SeedFile>(
            reader.ReadToEnd(), new JsonSerializerOptions(JsonSerializerDefaults.Web));

        return doc?.Tickers?
            .Select(t => t.Trim().ToUpperInvariant())
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList() ?? [];
    }

    private sealed record SeedFile(List<string> Tickers);
}
