namespace FinanceSentry.Tests.Unit.Architecture;

using System.Reflection;
using FluentAssertions;
using NetArchTest.Rules;
using Xunit;

/// <summary>
/// Namespace-level boundaries the .csproj reference graph (<see cref="ModuleBoundaryTests"/>)
/// cannot express: what a host, or the cross-module wiring layer, may reach into *inside* a
/// project it does legitimately reference. finance-sentry#502.
///
/// Mapping notes (solution layout as of this PR — see the PR body for the full writeup):
/// - "Hosts" = the deployable ASP.NET Core entry points: FinanceSentry.API, FinanceSentry.Mcp,
///   and FinanceSentry.Gateway. Gateway currently has zero module ProjectReferences (it is a
///   pure reverse proxy), so its check passes vacuously today; it stays in the list so a future
///   module reference is covered from day one. Gateway's DLL is loaded from its own build output
///   rather than via a ProjectReference from this test project — see the .csproj comment.
/// - Each module is one assembly (FinanceSentry.Modules.&lt;Name&gt;) with Domain/Application/
///   Infrastructure/API namespaces underneath — not separate per-layer assemblies. So "must not
///   touch Modules.*.Infrastructure.*" is a namespace check within an otherwise-legal assembly
///   reference, not a project-reference check (that's rule 501 already covers).
/// - "Integration" is the FinanceSentry.Integration project: adapters that implement one
///   module's Domain.Ports read-port interfaces by querying another module (the 039 pattern
///   referenced in ModuleBoundaryTests). The issue's "Integration is pinned to *.Domain.Ports"
///   is read as: Integration's own dependencies on OTHER modules should be limited to those
///   modules' Domain.Ports namespace, since Ports is the intentional seam.
/// </summary>
public class NamespaceBoundaryTests
{
    private const string ModulePrefix = "FinanceSentry.Modules.";

    // ---- Rule 1: hosts must not depend on a module's Infrastructure namespace ----
    //
    // Where a host reaches into Infrastructure today, it is because the composition root
    // genuinely needs a concrete type Infrastructure owns (a DbContext, a Hangfire job class) —
    // not because a small move would relocate the type to Application/Core. Restructuring these
    // is out of scope for this change; tracked in a follow-up issue instead of weakening the rule.
    // Keyed by (host assembly name, type full name) — API's and Gateway's top-level-statement
    // Program classes both resolve to the bare "Program" full name in the global namespace
    // (FinanceSentry.Mcp deliberately avoids this collision with a namespaced Program instead;
    // see the comment in FinanceSentry.Mcp/Program.cs), so the assembly name disambiguates them.
    private static readonly (string Assembly, string Type, string Reason)[] HostInfrastructureExceptions =
    [
        ("FinanceSentry.API", "Program",
            "wires up Hangfire via BankSync's AddHangfireServices(...) extension method at the composition " +
            "root; Hangfire's own setup (queues, storage, dashboard auth) is itself an infrastructure " +
            "concern, so there is no non-Infrastructure surface to call instead."),
        ("FinanceSentry.API", "FinanceSentry.API.Migrations.MigrationExtensions",
            "applies every module's EF Core migrations from the composition root at startup; " +
            "running a module's migrations requires its concrete DbContext, which only exists in " +
            "that module's Infrastructure namespace."),
        ("FinanceSentry.API", "FinanceSentry.API.Commands.RecategorizationCommand",
            "one-off maintenance CLI verb (not a web endpoint) that bulk-queries BankSync's " +
            "DbContext directly to drive a re-categorization pass; no Application-layer read " +
            "surface exists for that raw per-row query."),
        ("FinanceSentry.API", "FinanceSentry.API.Commands.RetentionCommand",
            "one-off maintenance CLI verb that resolves Retention's Hangfire job classes directly " +
            "to trigger them out of band; the job classes are the unit Hangfire schedules and " +
            "there is no Application-layer facade in front of them."),
        ("FinanceSentry.API", "FinanceSentry.API.Commands.SubscriptionDetectionCommand",
            "one-off maintenance CLI verb that resolves BankSync's SubscriptionDetectionJob " +
            "directly, for the same reason as RetentionCommand."),
        ("FinanceSentry.Mcp", "FinanceSentry.Mcp.Tools.GetSyncHealthTool",
            "reads raw sync-state columns from three modules' DbContexts directly to build a " +
            "per-provider health summary; no cross-module read port exposes sync health yet " +
            "(tracked in a follow-up issue)."),
    ];

    [Fact]
    public void Hosts_DoNotDependOnModuleInfrastructure()
    {
        var moduleNames = ModuleNames();
        moduleNames.Should().NotBeEmpty("the module assemblies must be discoverable for the boundary check to mean anything");

        var disallowed = moduleNames.Select(m => $"{ModulePrefix}{m}.Infrastructure").ToArray();
        var exceptions = HostInfrastructureExceptions.ToDictionary(e => (e.Assembly, e.Type), e => e.Reason);
        var seenExceptions = new HashSet<(string Assembly, string Type)>();

        var violations = new List<string>();
        foreach (var hostAssemblyName in new[] { "FinanceSentry.API", "FinanceSentry.Mcp", "FinanceSentry.Gateway" })
        {
            var assembly = LoadAssembly(hostAssemblyName);
            var flagged = Types.InAssembly(assembly)
                .That().HaveDependencyOnAny(disallowed)
                .GetTypes();

            foreach (var type in flagged)
            {
                var key = (hostAssemblyName, type.FullName!);
                if (exceptions.ContainsKey(key))
                {
                    seenExceptions.Add(key);
                    continue;
                }

                violations.Add(type.FullName!);
            }
        }

        violations.Should().BeEmpty(
            "a host must reach a module through its Application/API surface or a Core read port, never its " +
            "Infrastructure internals directly; add a named exception with a reason if this is a deliberate " +
            "composition-root carve-out: {0}", string.Join(", ", violations));

        exceptions.Keys.Except(seenExceptions).Should().BeEmpty(
            "these named exceptions no longer have a matching violation — the underlying code moved, so the " +
            "exception is stale and should be deleted: {0}", string.Join(", ", exceptions.Keys.Except(seenExceptions)));
    }

    // ---- Rule 2: a module's Domain namespace stays persistence-free ----

    [Fact]
    public void ModuleDomains_StayPersistenceFree()
    {
        var moduleNames = ModuleNames();
        moduleNames.Should().NotBeEmpty("the module assemblies must be discoverable for the boundary check to mean anything");

        var violations = new List<string>();
        foreach (var moduleName in moduleNames)
        {
            var moduleNamespace = $"{ModulePrefix}{moduleName}";
            var assembly = LoadAssembly(moduleNamespace);

            var disallowed = new[]
            {
                "Microsoft.EntityFrameworkCore",
                "Npgsql",
                $"{moduleNamespace}.Infrastructure",
            };

            var flagged = Types.InAssembly(assembly)
                .That().ResideInNamespaceStartingWith($"{moduleNamespace}.Domain")
                .And().HaveDependencyOnAny(disallowed)
                .GetTypes();

            violations.AddRange(flagged.Select(t => t.FullName!));
        }

        violations.Should().BeEmpty(
            "Domain must stay persistence-free: no EF Core / Npgsql types and no dependency on the owning " +
            "module's own Infrastructure namespace. Move the persistence concern behind a repository " +
            "interface defined in Domain and implemented in Infrastructure: {0}", string.Join(", ", violations));
    }

    // ---- Rule 3: Integration reaches other modules only through their Domain.Ports namespace ----
    //
    // Reality check (see the PR body): most of today's adapters in FinanceSentry.Integration also
    // reach a module's Application query handlers, API response DTOs, or Domain.Repositories —
    // not just its Domain.Ports interfaces. Narrowing every one of those to a dedicated port is
    // real module restructuring, explicitly out of scope here. The rule stays at its intended
    // strength (Domain.Ports only) with one named exception per adapter class and a single
    // follow-up issue (finance-sentry#673) to burn the list down, rather than being loosened to
    // match what exists.
    private static readonly (string Type, string Reason)[] IntegrationPortPinningExceptions =
    [
        ("FinanceSentry.Integration.AssetSignalAdapter",
            "reads Radar's query handler and a Radar Domain.MarketStructure type directly; no " +
            "Domain.Ports read surface exists yet for asset-signal reads."),
        ("FinanceSentry.Integration.EventsCorporateCalendarAdapter",
            "reads Research's query handler and API response DTOs directly instead of a port."),
        ("FinanceSentry.Integration.EventsDeliveryAdapter",
            "reads Companion's Application service and Domain/Domain.Repositories types directly " +
            "instead of a port."),
        ("FinanceSentry.Integration.EventsFiredAlertAdapter",
            "reads Alerts' query handler directly instead of a port."),
        ("FinanceSentry.Integration.EventsMacroEventAdapter",
            "depends on Research's concrete Application service interface instead of a port."),
        ("FinanceSentry.Integration.EventsPeriodicFilingAdapter",
            "depends on Research's concrete Application service interface, and on a domain helper " +
            "type from Events' own Domain root, instead of a port."),
        ("FinanceSentry.Integration.EventsThesisCatalystAdapter",
            "depends on Research's Domain.Repositories interface directly instead of a port."),
        ("FinanceSentry.Integration.HoldingTaxLotsAdapter",
            "reads BrokerageSync's query handler directly instead of a port."),
        ("FinanceSentry.Integration.IpsAllocationPolicySource",
            "reads Research's query handler and API response DTOs directly instead of a port."),
        ("FinanceSentry.Integration.PortfolioScanDataReader",
            "reads Research's and Risk's query handlers, API response DTOs, and Domain.Repositories " +
            "interfaces directly instead of a port."),
        ("FinanceSentry.Integration.RadarPortfolioValueSource",
            "depends on Wealth's Domain.Repositories interface directly instead of a port."),
        ("FinanceSentry.Integration.ResearchTrackRecordSource",
            "reads Research's query handler and API response DTOs directly instead of a port."),
        ("FinanceSentry.Integration.RiskPositionCapSource",
            "reads Risk's query handler directly instead of a port."),
    ];

    [Fact]
    public void Integration_ReachesModulesOnlyThroughDomainPorts()
    {
        var moduleNames = ModuleNames();
        moduleNames.Should().NotBeEmpty("the module assemblies must be discoverable for the boundary check to mean anything");

        var disallowed = new List<string>();
        foreach (var moduleName in moduleNames)
        {
            var moduleNamespace = $"{ModulePrefix}{moduleName}";
            var portsNamespace = $"{moduleNamespace}.Domain.Ports";
            var moduleAssembly = LoadAssembly(moduleNamespace);

            // Every distinct namespace the module actually has types in, minus the allowed
            // Domain.Ports subtree, minus the module's own bare root and bare Domain root (both
            // are namespace-tree ancestors of Domain.Ports and would shadow it as a false match
            // if included — see NetArchTest's prefix-matching namespace tree).
            var moduleNonPortsNamespaces = moduleAssembly.GetTypes()
                .Select(t => t.Namespace)
                .Where(ns => ns is not null && (ns == moduleNamespace || ns.StartsWith(moduleNamespace + ".", StringComparison.Ordinal)))
                .Distinct()
                .Where(ns => ns != moduleNamespace
                    && ns != $"{moduleNamespace}.Domain"
                    && ns != portsNamespace
                    && !ns!.StartsWith(portsNamespace + ".", StringComparison.Ordinal));

            disallowed.AddRange(moduleNonPortsNamespaces!);
        }

        var integrationAssembly = LoadAssembly("FinanceSentry.Integration");
        var exceptions = IntegrationPortPinningExceptions.ToDictionary(e => e.Type, e => e.Reason);
        var seenExceptions = new HashSet<string>();

        var violations = new List<string>();
        var flagged = Types.InAssembly(integrationAssembly)
            .That().HaveDependencyOnAny(disallowed.ToArray())
            .GetTypes();

        foreach (var type in flagged)
        {
            if (exceptions.ContainsKey(type.FullName!))
            {
                seenExceptions.Add(type.FullName!);
                continue;
            }

            violations.Add(type.FullName!);
        }

        violations.Should().BeEmpty(
            "Integration should reach another module only through its Domain.Ports read-port interfaces, " +
            "the seam the 039 pattern was built around; add a named exception with a reason if this is a " +
            "deliberate, tracked carve-out: {0}", string.Join(", ", violations));

        exceptions.Keys.Except(seenExceptions).Should().BeEmpty(
            "these named exceptions no longer have a matching violation — the underlying code moved, so the " +
            "exception is stale and should be deleted: {0}", string.Join(", ", exceptions.Keys.Except(seenExceptions)));
    }

    private static string[] ModuleNames()
    {
        var srcDir = Path.Combine(FindBackendRoot(), "src");
        return Directory.GetDirectories(srcDir, $"{ModulePrefix}*")
            .Select(d => Path.GetFileName(d)[ModulePrefix.Length..])
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();
    }

    private static Assembly LoadAssembly(string simpleName)
    {
        var path = Directory.GetFiles(AppContext.BaseDirectory, $"{simpleName}.dll").SingleOrDefault()
            ?? FindInOwnBuildOutput(simpleName);

        path.Should().NotBeNull(
            "{0}.dll must be present either in the test output directory (via a ProjectReference) or " +
            "buildable from its own project under src/, for this check to mean anything", simpleName);

        return Assembly.LoadFrom(path!);
    }

    // FinanceSentry.Gateway is deliberately not a ProjectReference of this test project (see the
    // .csproj comment: it would pull a BouncyCastle package version that collides with one this
    // project already uses), so its DLL is picked up from its own build output instead.
    private static string? FindInOwnBuildOutput(string simpleName)
    {
        var projectDir = Path.Combine(FindBackendRoot(), "src", simpleName);
        if (!Directory.Exists(projectDir))
        {
            return null;
        }

        return Directory.GetFiles(projectDir, $"{simpleName}.dll", SearchOption.AllDirectories)
            .Where(p => p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private static string FindBackendRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FinanceSentry.sln")))
            dir = dir.Parent;

        return dir?.FullName
            ?? throw new InvalidOperationException("FinanceSentry.sln not found above the test bin directory.");
    }
}
