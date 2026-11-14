namespace FinanceSentry.Modules.Research.Infrastructure.Sources;

using System.Reflection;
using System.Text.Json;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.Domain.ReferenceUniverse;
using FinanceSentry.Modules.Research.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

/// <summary>
/// The S&amp;P 500 port. A consumer of the reference-universe table (list <c>sp500</c>, refreshed weekly from its
/// upstream), with the embedded seed as the fallback whenever the table is empty, implausibly small or
/// unreadable - so a fresh database, a failed first refresh or a database blip never empties the broad
/// universe. Registered as a singleton; the answer is cached for <see cref="CacheTtl"/> (the table changes
/// weekly), shorter after a fallback so a recovered table is picked up quickly.
/// </summary>
public sealed class Sp500ConstituentSource(
    IServiceScopeFactory scopes,
    TimeProvider time,
    ILogger<Sp500ConstituentSource> logger) : IIndexConstituentSource
{
    public static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan FallbackCacheTtl = TimeSpan.FromMinutes(2);

    /// <summary>A stored list smaller than this is not an S&amp;P 500; the seed is used instead.</summary>
    public const int MinTableMembers = 400;

    private const string ResourceName =
        "FinanceSentry.Modules.Research.Infrastructure.Resources.sp500-constituents.json";

    private static readonly Lazy<IReadOnlyList<string>> Embedded = new(LoadEmbedded, isThreadSafe: true);

    private readonly Lock gate = new();
    private IReadOnlyList<string>? cached;
    private DateTimeOffset expiresAt;

    /// <summary>The embedded seed alone (the fallback), upper-cased and de-duplicated.</summary>
    public static IReadOnlyList<string> EmbeddedSeed => Embedded.Value;

    public IReadOnlyList<string> GetConstituents()
    {
        var now = time.GetUtcNow();
        lock (gate)
        {
            if (cached is not null && now < expiresAt)
            {
                return cached;
            }
        }

        var (list, fromTable) = Resolve();
        lock (gate)
        {
            cached = list;
            expiresAt = now + (fromTable ? CacheTtl : FallbackCacheTtl);
        }

        return list;
    }

    private (IReadOnlyList<string> Tickers, bool FromTable) Resolve()
    {
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ResearchDbContext>();
            var tickers = db.ReferenceListMembers.AsNoTracking()
                .Where(m => m.ListKey == ReferenceListKeys.Sp500)
                .Select(m => m.Ticker)
                .ToList()
                .Distinct(StringComparer.Ordinal)
                .OrderBy(t => t, StringComparer.Ordinal)
                .ToList();

            if (tickers.Count >= MinTableMembers)
            {
                return (tickers, true);
            }

            logger.LogWarning(
                "Reference universe holds {Count} S&P 500 members (fewer than {Min}); serving the embedded seed",
                tickers.Count, MinTableMembers);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Reference universe S&P 500 list unreadable; serving the embedded seed");
        }

        return (EmbeddedSeed, false);
    }

    private static IReadOnlyList<string> LoadEmbedded()
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
