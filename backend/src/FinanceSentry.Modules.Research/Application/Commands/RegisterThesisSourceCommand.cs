namespace FinanceSentry.Modules.Research.Application.Commands;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Repositories;

/// <summary>
/// Registers (or re-enables) an external news source (feature 030, FR-007). <see cref="ThesisId"/>
/// null registers a market-wide source. Idempotent by URL — re-registering an existing URL updates
/// its thesis/keywords and re-enables it rather than creating a duplicate.
/// </summary>
public record RegisterThesisSourceCommand(
    Guid? ThesisId,
    string Name,
    string Url,
    string Kind,
    IReadOnlyList<string>? Keywords) : ICommand<RegisteredSourceDto>;

public class RegisterThesisSourceCommandHandler(INewsSourceRepository repo)
    : ICommandHandler<RegisterThesisSourceCommand, RegisteredSourceDto>
{
    public async Task<RegisteredSourceDto> Handle(RegisterThesisSourceCommand cmd, CancellationToken ct)
    {
        var name = cmd.Name?.Trim() ?? string.Empty;
        var url = cmd.Url?.Trim() ?? string.Empty;
        if (name.Length == 0 || url.Length == 0)
        {
            throw new ArgumentException("News source name and url are required.");
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out _))
        {
            throw new ArgumentException($"News source url '{url}' is not a valid absolute URL.");
        }

        var kind = Enum.TryParse<NewsSourceKind>(cmd.Kind, ignoreCase: true, out var parsed)
            ? parsed
            : throw new ArgumentException($"Unknown news source kind '{cmd.Kind}'. Expected Rss or Page.");

        var keywords = cmd.Keywords?
            .Select(k => k.Trim())
            .Where(k => k.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];

        var existing = await repo.GetByUrlAsync(url, ct);
        if (existing is not null)
        {
            existing.Name = name;
            existing.Kind = kind;
            existing.Keywords = keywords;
            existing.ThesisId = cmd.ThesisId;

            // Re-registering is a deliberate "try this again", so the failure history goes with it.
            // Leaving the counter alone meant a source past DisableThreshold was re-retired by its
            // very first failure and could never actually come back (issue #318).
            NewsSourceHealthTracker.ClearFailures(existing);
            existing.RetiredAt = null;
            existing.RetiredReason = null;
            await repo.UpdateAsync(existing, ct);
            return new RegisteredSourceDto(existing.Id, existing.Enabled);
        }

        var source = new NewsSource
        {
            Name = name,
            Kind = kind,
            Url = url,
            Keywords = keywords,
            ThesisId = cmd.ThesisId,
            Enabled = true,
        };

        var id = await repo.AddAsync(source, ct);
        return new RegisteredSourceDto(id, source.Enabled);
    }
}
