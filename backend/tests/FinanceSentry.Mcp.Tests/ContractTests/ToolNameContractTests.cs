using FluentAssertions;
using Xunit;

namespace FinanceSentry.Mcp.Tests.ContractTests;

public sealed class ToolNameContractTests
{
    private static readonly IReadOnlySet<string> AgreedToolSurface = new HashSet<string>
    {
        "acknowledge_companion_events",
        "acknowledge_risk_violation",
        "check_risk_rules",
        "committed_merchants",
        "delete_thesis",
        "describe_query_schema",
        "get_account_summary",
        "get_allocation_vs_target",
        "get_book_performance",
        "get_budget_status",
        "get_cashflow_report",
        "get_crypto_pnl_detail",
        "get_analyst_actions",
        "get_earnings_calendar",
        "get_fundamentals",
        "get_ips",
        "get_macro_calendar",
        "get_market_breadth",
        "get_market_regime",
        "get_market_structure",
        "get_net_worth_history",
        "get_news_for_ticker",
        "get_portfolio_snapshot",
        "get_postmortem_packet",
        "get_quotes",
        "get_radar_summary",
        "get_recent_filings",
        "get_research_context",
        "get_risk_rules",
        "get_sync_health",
        "get_tax_lots",
        "get_thesis_performance",
        "get_notification_mode",
        "get_pending_companion_events",
        "get_track_record",
        "get_relative_strength",
        "get_sector_rotation",
        "get_valuation_snapshot",
        "list_active_alerts",
        "list_candidates",
        "list_news_sources",
        "list_signals",
        "list_subscriptions",
        "list_theses",
        "list_thesis_breaks",
        "list_thesis_events",
        "list_transactions",
        "register_thesis_source",
        "run_analytics_query",
        "run_thesis_monitor",
        "promote_candidate",
        "reject_candidate",
        "score_candidate",
        "save_ips",
        "save_risk_rules",
        "save_thesis",
        "search_market_news",
        "search_research_corpus",
        "set_notification_mode",
        "watchlist",
    };

    [Fact]
    public void ToolNames_MatchAgreedSurface()
    {
        var actual = McpToolReflection.GetToolNames().ToHashSet(StringComparer.Ordinal);

        actual.Should().BeEquivalentTo(
            AgreedToolSurface,
            because: "the MCP tool surface must match the agreed 60-tool contract — no more, no fewer");
    }
}
