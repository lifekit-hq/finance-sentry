namespace FinanceSentry.Modules.Research.Tests.Recommendations;

using System.Reflection;
using System.Text.RegularExpressions;
using FinanceSentry.Modules.Research.API.Controllers;
using FinanceSentry.Modules.Research.Application.Commands;
using FinanceSentry.Modules.Research.Application.Queries;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Infrastructure.Jobs;
using FinanceSentry.Modules.Research.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Xunit;

/// <summary>
/// The no-trade wall (#1069): Finance Sentry never places, stages or transmits an order. A confirm only records the
/// owner's decision and shows order text they place at the broker themselves. These tests pin that every piece of the
/// decision loop depends only on read ports and the journal - adding a dependency that could reach a broker fails here
/// and has to be argued for in review.
/// </summary>
public sealed class RecommendationNoTradeWallTests
{
    private static readonly Type[] DecisionLoop =
    [
        typeof(ProposeRecommendationCommandHandler),
        typeof(DecideRecommendationCommandHandler),
        typeof(ExpireRecommendationsCommandHandler),
        typeof(RunRecommendationTriggersCommandHandler),
        typeof(ScoreRecommendationOutcomesCommandHandler),
        typeof(GetRecommendationsQueryHandler),
        typeof(RecommendationPositionReader),
        typeof(RecommendationWriter),
        typeof(RecommendationRepository),
        typeof(RecommendationTriggerJob),
        typeof(RecommendationExpiryJob),
        typeof(RecommendationOutcomeJob),
        typeof(RecommendationsController),
    ];

    /// <summary>Read ports, the journal, the recommendation store and the loop's own pieces - nothing that writes outward.</summary>
    private static readonly HashSet<string> AllowedDependencies =
    [
        "IThesisRepository",
        "IThesisEventRepository",
        "IThesisEventRecorder",
        "IRecommendationRepository",
        "IPositionCapSource",
        "IAllocationDriftReader",
        "IAssetSignalReader",
        "IFiredEventSource",
        "IRiskPolicyGate",
        "IPolicyAckReader",
        "IBookFiguresService",
        "IMarketDataService",
        "IPositionLedgerSource",
        "ResearchDbContext",
        "RecommendationPositionReader",
        "RecommendationWriter",
        "ICommandHandler`2",
        "IQueryHandler`2",
        "ILogger`1",
    ];

    private static readonly HashSet<string> BrokerWords = ["Broker", "Brokerage", "Ibkr", "Binance", "Order", "Orders", "Trade", "Trades", "Trading", "Http"];

    [Fact]
    public void Every_piece_of_the_decision_loop_depends_only_on_read_ports_and_the_journal()
    {
        var unexpected = DecisionLoop
            .SelectMany(t => t.GetConstructors().SelectMany(c => c.GetParameters()).Select(p => (Owner: t.Name, Type: p.ParameterType)))
            .Where(d => !AllowedDependencies.Contains(d.Type.IsGenericType ? d.Type.GetGenericTypeDefinition().Name : d.Type.Name))
            .Select(d => $"{d.Owner} <- {d.Type.Name}")
            .ToList();

        unexpected.Should().BeEmpty("the decision loop records decisions; it never reaches a broker");
    }

    [Fact]
    public void The_command_and_query_handlers_the_loop_dispatches_are_its_own()
    {
        var dispatched = DecisionLoop
            .SelectMany(t => t.GetConstructors().SelectMany(c => c.GetParameters()))
            .Select(p => p.ParameterType)
            .Where(t => t.IsGenericType && t.Name is "ICommandHandler`2" or "IQueryHandler`2")
            .Select(t => t.GetGenericArguments()[0].Name)
            .ToList();

        dispatched.Should().OnlyContain(name => name.Contains("Recommendation"));
    }

    [Fact]
    public void No_dependency_of_the_loop_names_a_broker_an_order_or_an_http_client()
        => DecisionLoop
            .SelectMany(t => t.GetConstructors().SelectMany(c => c.GetParameters()))
            .SelectMany(p => p.ParameterType.IsGenericType ? p.ParameterType.GetGenericArguments().Append(p.ParameterType) : [p.ParameterType])
            .Select(t => t.Name)
            .Where(name => !name.Contains("Recommendation"))
            .Should().NotContain(name => Words(name).Any(BrokerWords.Contains));

    private static IEnumerable<string> Words(string typeName) => Regex.Matches(typeName, "[A-Z][a-z0-9]*").Select(m => m.Value);

    [Fact]
    public void The_research_module_does_not_reference_any_broker_sync_module()
        => typeof(RecommendationWriter).Assembly.GetReferencedAssemblies().Select(a => a.Name)
            .Should().NotContain(name => name!.Contains("BrokerageSync") || name.Contains("CryptoSync") || name.Contains("BankSync"));

    [Fact]
    public void The_api_offers_reading_and_recording_a_verdict_and_nothing_else()
    {
        var routes = typeof(RecommendationsController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(m => m.Name)
            .ToList();

        routes.Should().BeEquivalentTo(["List", "Confirm", "Reject"]);
    }
}
