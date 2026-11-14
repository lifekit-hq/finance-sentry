namespace FinanceSentry.Modules.Research.Application.Queries;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.Application.Services.Catalyst;
using FinanceSentry.Modules.Research.Domain.Catalyst;

/// <summary>The stored GDELT attention measure (discovery funnel stage 7): per theme phrase, the newest complete day's article count against the days before it.</summary>
public sealed record GetThemeAttentionQuery(string? Theme = null) : IQuery<ThemeAttentionView>;

public sealed record ThemeAttentionView(
    string? Theme,
    int Count,
    IReadOnlyList<ThemeAttentionReading> Readings,
    string? Note);

public sealed class GetThemeAttentionQueryHandler(IThemeAttentionReader reader)
    : IQueryHandler<GetThemeAttentionQuery, ThemeAttentionView>
{
    public async Task<ThemeAttentionView> Handle(GetThemeAttentionQuery query, CancellationToken ct)
    {
        var readings = await reader.GetReadingsAsync(query.Theme, ct);
        var note = readings.Count == 0
            ? "No attention reading is stored" + (string.IsNullOrWhiteSpace(query.Theme) ? "." : $" for '{query.Theme.Trim()}'.")
                + " The daily refresh fills it; a theme with no phrase in Catalyst:ThemePhrases is not measured."
            : null;
        return new ThemeAttentionView(string.IsNullOrWhiteSpace(query.Theme) ? null : query.Theme.Trim(), readings.Count, readings, note);
    }
}
