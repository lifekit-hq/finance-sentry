namespace FinanceSentry.Tests.Unit.Architecture;

using System.Net;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Companion.Application.Services;
using FinanceSentry.Modules.Companion.Domain;
using FinanceSentry.Modules.Companion.Infrastructure.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;
using Xunit;

/// <summary>
/// Constitution CC10 (Principle VII): no endpoint, MCP tool or field is named after Ledger, and the hook payload
/// carries ids only. The rules live in <see cref="LedgerBoundaryRules"/>; the fixtures prove they block and allow,
/// and the surface tests run them over the real controllers, MCP tools and wake payloads.
/// </summary>
public class LedgerBoundaryTests
{
    // Tools named after Ledger before CC10 had a check. Renaming one breaks the agent's tool contract, so each is
    // listed here until a coordinated rename lands; the list may only shrink (a new entry fails review).
    private static readonly string[] LegacyLedgerToolNames =
    [
        "get_ledger_scoreboard", "list_ledger_corrections", "record_ledger_correction",
    ];

    // --- Fixtures: the rules block what they must and allow what they must. ---

    [Fact]
    public void Blocked_fixture_is_flagged_on_every_clause()
    {
        LedgerBoundaryRules.NamingViolations("route", [("LedgerController", "api/ledger/scoreboard")])
            .Should().ContainSingle();
        LedgerBoundaryRules.NamingViolations("MCP tool", [("Tools", "ask_Ledger_agent")])
            .Should().ContainSingle();
        LedgerBoundaryRules.PayloadViolations(["eventId", "ledgerNote"])
            .Should().ContainSingle(v => v.Contains("ledgerNote") && v.Contains("named after Ledger"));
        LedgerBoundaryRules.PayloadViolations(["eventId", "amount", "narrative"])
            .Should().HaveCount(2, "amounts and narrative are not ids");
    }

    [Fact]
    public void Allowed_fixture_passes()
    {
        LedgerBoundaryRules.NamingViolations("route", [("AlertsController", "api/alerts/{id}/acknowledge")])
            .Should().BeEmpty();
        LedgerBoundaryRules.NamingViolations("MCP tool", [("Tools", "get_net_worth_history")])
            .Should().BeEmpty();
        LedgerBoundaryRules.PayloadViolations(["eventId", "userId", "kind", "referenceId", "appUrl"])
            .Should().BeEmpty();
    }

    // --- Surfaces: the real code. ---

    [Fact]
    public void No_endpoint_route_is_named_after_Ledger()
    {
        var routes = ControllerRoutes().ToList();

        routes.Should().NotBeEmpty("the controllers must be discoverable for the check to mean anything");
        LedgerBoundaryRules.NamingViolations("route", routes).Should().BeEmpty();
    }

    [Fact]
    public void No_MCP_tool_or_tool_parameter_is_named_after_Ledger_beyond_the_legacy_list()
    {
        var tools = McpTools().ToList();

        tools.Should().NotBeEmpty("the MCP tools must be discoverable for the check to mean anything");

        var names = tools.Select(t => (t.Owner, Name: t.Tool)).ToList();
        var parameters = tools.SelectMany(t => t.Parameters.Select(p => (Owner: $"{t.Owner}.{t.Tool}", Name: p))).ToList();

        LedgerBoundaryRules.NamingViolations("MCP tool", names.Where(n => !LegacyLedgerToolNames.Contains(n.Name)))
            .Concat(LedgerBoundaryRules.NamingViolations("MCP tool parameter", parameters))
            .Should().BeEmpty();
        names.Select(n => n.Name).Where(LedgerBoundaryRules.IsNamedAfterLedger)
            .Should().BeSubsetOf(LegacyLedgerToolNames, "a legacy entry that no longer exists must be removed from the list");
    }

    [Fact]
    public async Task Wake_payload_carries_ids_only_for_every_event_kind_and_the_digest()
    {
        var handler = new CapturingHandler();
        var dispatcher = new WebhookAgentWakeDispatcher(
            new FakeHttpFactory(handler),
            Options.Create(new CompanionOptions
            {
                AgentTriggerUrl = "http://agent.local/trigger",
                PublicBaseUrl = "https://app.example.test",
            }),
            new AllowAll(),
            NullLogger<WebhookAgentWakeDispatcher>.Instance);

        foreach (var kind in Enum.GetValues<CompanionEventKind>())
        {
            await dispatcher.WakeAsync(new CompanionEvent
            {
                UserId = Guid.NewGuid(),
                Kind = kind,
                Subject = "MU",
                Severity = "critical",
                Summary = "narrative that must stay behind MCP",
                ReferenceId = Guid.NewGuid(),
                AppPath = "/holdings",
                OccurredAt = DateTimeOffset.UtcNow,
            });
        }

        await dispatcher.WakeDigestAsync(Guid.NewGuid(), heldCount: 3);

        handler.Bodies.Should().HaveCount(Enum.GetValues<CompanionEventKind>().Length + 1);
        foreach (var body in handler.Bodies)
        {
            using var doc = JsonDocument.Parse(body);
            LedgerBoundaryRules.PayloadViolations(doc.RootElement.EnumerateObject().Select(p => p.Name))
                .Should().BeEmpty($"payload {body} must carry ids only");
            body.Should().NotContain("narrative that must stay behind MCP");
        }
    }

    // --- Reflection over the real surfaces. ---

    private static IEnumerable<Assembly> ProductAssemblies()
    {
        foreach (var path in Directory.GetFiles(AppContext.BaseDirectory, "FinanceSentry.*.dll"))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            if (name.Contains("Tests", StringComparison.Ordinal))
            {
                continue;
            }

            yield return AssemblyLoadContext.Default.Assemblies.FirstOrDefault(a => a.GetName().Name == name)
                ?? AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
        }
    }

    private static IEnumerable<(string Owner, string Name)> ControllerRoutes()
    {
        var controllers = ProductAssemblies()
            .SelectMany(a => a.GetTypes())
            .Where(t => !t.IsAbstract && typeof(ControllerBase).IsAssignableFrom(t));

        foreach (var controller in controllers)
        {
            var controllerName = controller.Name.EndsWith("Controller", StringComparison.Ordinal)
                ? controller.Name[..^"Controller".Length]
                : controller.Name;
            var prefixes = controller.GetCustomAttributes<RouteAttribute>(inherit: true)
                .Select(r => r.Template ?? string.Empty)
                .DefaultIfEmpty(string.Empty)
                .ToList();

            foreach (var action in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                foreach (var template in action.GetCustomAttributes().OfType<IRouteTemplateProvider>())
                {
                    foreach (var prefix in prefixes)
                    {
                        var route = $"{prefix}/{template.Template}"
                            .Replace("[controller]", controllerName, StringComparison.OrdinalIgnoreCase)
                            .Replace("[action]", action.Name, StringComparison.OrdinalIgnoreCase)
                            .Trim('/');
                        yield return ($"{controller.Name}.{action.Name}", route);
                    }
                }
            }

            // The controller name is itself an endpoint segment wherever a route uses [controller].
            yield return (controller.Name, controllerName);
        }
    }

    private static IEnumerable<(string Owner, string Tool, IReadOnlyList<string> Parameters)> McpTools()
    {
        var types = typeof(FinanceSentry.Mcp.Tools.GetAccountSummaryTool).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && t.GetCustomAttribute<McpServerToolTypeAttribute>() is not null);

        foreach (var type in types)
        {
            foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public))
            {
                if (method.GetCustomAttribute<McpServerToolAttribute>() is { } tool)
                {
                    yield return (
                        type.Name,
                        tool.Name ?? method.Name,
                        method.GetParameters().Select(p => p.Name ?? string.Empty).ToList());
                }
            }
        }
    }

    private sealed class AllowAll : IUserAuthorizationChecker
    {
        public Task<bool> IsAuthorizedAsync(Guid userId, string policy, CancellationToken ct = default)
            => Task.FromResult(true);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Bodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed class FakeHttpFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler);
    }
}
