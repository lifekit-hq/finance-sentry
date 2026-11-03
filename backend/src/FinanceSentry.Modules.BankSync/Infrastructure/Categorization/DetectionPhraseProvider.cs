namespace FinanceSentry.Modules.BankSync.Infrastructure.Categorization;

using FinanceSentry.Modules.BankSync.Application.Services.DetectionPhrases;
using FinanceSentry.Modules.BankSync.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Singleton provider backed by an in-memory snapshot of <c>detection_phrases</c>, loaded lazily on
/// first use (thread-safe) the way <see cref="CategoryResolver"/> loads its tables. The snapshot is
/// taken once per process: an edited row takes effect on the next API restart, as a
/// <c>merchant_keywords</c> edit does.
/// </summary>
public sealed class DetectionPhraseProvider(IServiceScopeFactory scopeFactory) : IDetectionPhraseProvider
{
    private readonly object _gate = new();
    private DetectionPhraseSet? _current;

    public DetectionPhraseSet Current
    {
        get
        {
            if (_current is { } loaded)
                return loaded;

            lock (_gate)
            {
                return _current ??= Load();
            }
        }
    }

    private DetectionPhraseSet Load()
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BankSyncDbContext>();

        // Id order is the order the seed lists the rows, and rows of a kind are tried in that order.
        var entries = db.DetectionPhrases.AsNoTracking()
            .OrderBy(p => p.Id)
            .Select(p => new DetectionPhraseEntry(p.Kind, p.Phrase, p.Provider, p.Locale, p.MccCondition, p.CategoryCondition))
            .ToList();

        return new DetectionPhraseSet(entries);
    }
}
