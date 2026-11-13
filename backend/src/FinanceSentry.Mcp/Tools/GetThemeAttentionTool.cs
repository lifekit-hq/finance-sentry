using System.ComponentModel;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.Application.Queries;
using ModelContextProtocol.Server;

namespace FinanceSentry.Mcp.Tools;

[McpServerToolType]
public sealed class GetThemeAttentionTool(IQueryHandler<GetThemeAttentionQuery, ThemeAttentionView> handler)
{
    [McpServerTool(Name = "get_theme_attention")]
    [Description("The stored news-attention measure per theme of the reference universe (memory, ai-infra, power, semiconductors, energy): for each phrase that means the theme in the press, the number of articles GDELT counted on the newest complete UTC day (`articles`, `asOf`) against the mean of the days before it (`baselineDailyArticles` over `baselineDays`; withheld when fewer than 5 days are stored) and their `ratio`. A ratio well above 1 is a crowded story, the lane the add rule should not chase: attention is a staging input, never the reason to add a name. Candidate scorecards carry the same readings for the themes the ticker is on (evidence.attention). With `theme` (id such as 'memory' or key 'theme:memory') only that theme; without it every measured theme. A soft signal: GDELT states no licence or SLA, and a day's count can move when GDELT re-indexes. Shared market data, not the owner's holdings. Read-only.")]
    public async Task<ThemeAttentionView> ExecuteAsync(
        [Description("Optional theme id ('memory') or list key ('theme:memory'); omit for every measured theme.")] string? theme = null,
        CancellationToken cancellationToken = default)
        => await handler.Handle(new GetThemeAttentionQuery(theme), cancellationToken);
}
