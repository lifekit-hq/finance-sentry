namespace FinanceSentry.Modules.Retention.Tests;

using System.Reflection;
using FinanceSentry.Modules.Retention.Application;
using FinanceSentry.Modules.Retention.Domain;
using FinanceSentry.Modules.Retention.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

/// <summary>
/// FR-001 enforcement (feature 024): the compiled registry is the single source of truth for every
/// table's retention decision, and these guards fail CI if a future change leaves a table ungoverned or
/// accidentally marks a user-owned financial table for deletion.
/// </summary>
public sealed class RetentionPolicyRegistryTests
{
    private static readonly IReadOnlyList<RetentionPolicy> Policies = RetentionPolicyRegistry.All;

    [Fact]
    public void Coverage_guard_every_module_DbContext_is_acknowledged()
    {
        // Force-load every module assembly (mirrors the app's module scan) so reflection sees them all.
        foreach (var dll in Directory.GetFiles(AppContext.BaseDirectory, "FinanceSentry.Modules.*.dll"))
        {
            var name = AssemblyName.GetAssemblyName(dll);
            if (AppDomain.CurrentDomain.GetAssemblies().All(a => a.GetName().Name != name.Name))
                Assembly.Load(name);
        }

        var contexts = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => a.GetName().Name?.StartsWith("FinanceSentry.Modules.") == true)
            .SelectMany(SafeTypes)
            .Where(t => typeof(DbContext).IsAssignableFrom(t) && t is { IsAbstract: false })
            .Select(t => t.Name)
            .ToHashSet();

        contexts.Should().NotBeEmpty();
        // A new module/context must be added to KnownContexts (and its tables to the registry) — this
        // is what surfaces "a table added by a future feature without a policy" (spec edge case).
        contexts.Should().BeSubsetOf(RetentionPolicyRegistry.KnownContexts,
            "every module DbContext must be acknowledged in the retention registry");
    }

    [Fact]
    public void No_duplicate_table_policies()
    {
        Policies.Select(p => p.QualifiedName)
            .Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Purge_and_downsample_policies_are_well_formed()
    {
        foreach (var p in Policies.Where(p => p.Action != RetentionAction.Keep))
        {
            p.TimestampColumn.Should().NotBeNullOrWhiteSpace(
                "{0} is {1} and needs a cutoff column", p.QualifiedName, p.Action);

            if (p.Enforcer == RetentionEnforcer.Generic)
                p.WindowDays.Should().NotBeNull("generic {0} needs a window", p.QualifiedName);
        }
    }

    [Fact]
    public void Keep_policies_carry_no_window()
    {
        foreach (var p in Policies.Where(p => p.Action == RetentionAction.Keep))
        {
            p.WindowDays.Should().BeNull();
            p.TimestampColumn.Should().BeNull();
        }
    }

    [Fact]
    public void Bespoke_policies_name_the_owning_job()
    {
        foreach (var p in Policies.Where(p => p.Enforcer == RetentionEnforcer.Bespoke))
            p.BespokeJobName.Should().NotBeNullOrWhiteSpace(
                "{0} is enforced elsewhere and must name its job", p.QualifiedName);
    }

    [Theory]
    // The FR-008 keep-forever whitelist: user-initiated financial records must never be purged.
    [InlineData("bank_sync", "Transactions")]
    [InlineData("bank_sync", "BankAccounts")]
    [InlineData("research", "theses")]
    [InlineData("research", "investment_policy_statements")]
    [InlineData("research", "watchlist_items")]
    [InlineData("budgets", "budgets")]
    [InlineData("crypto_sync", "CryptoHoldings")]
    [InlineData("brokerage_sync", "BrokerageHoldings")]
    [InlineData("risk", "risk_rule_sets")]
    public void User_owned_tables_are_keep_forever(string schema, string table)
    {
        var policy = Policies.SingleOrDefault(p => p.Schema == schema && p.Table == table);
        policy.Should().NotBeNull("{0}.{1} must have a policy", schema, table);
        policy!.Action.Should().Be(RetentionAction.Keep, "{0}.{1} is user-owned financial data", schema, table);
    }

    [Theory]
    // The governed growing tables the generic engine actively purges (research D9).
    [InlineData("bank_sync", "audit_logs", "PerformedAt")]
    [InlineData("bank_sync", "SyncJobs", "CreatedAt")]
    [InlineData("analytics", "query_audit", "CreatedAt")]
    [InlineData("companion", "companion_events", "CapturedAt")]
    [InlineData("research", "candidate_scores", "ScoredAt")]
    [InlineData("research", "valuation_snapshots", "CapturedAt")]
    [InlineData("risk", "holding_snapshots", "CapturedAt")]
    public void Governed_growing_tables_are_generic_purge(string schema, string table, string tsColumn)
    {
        var policy = Policies.Single(p => p.Schema == schema && p.Table == table);
        policy.IsGenericPurge.Should().BeTrue();
        policy.TimestampColumn.Should().Be(tsColumn);
        policy.WindowDays.Should().BePositive();
    }

    [Fact]
    public void Generic_purge_set_matches_expected_count()
    {
        // Guards against accidental additions/removals to the actively-purged set.
        RetentionPolicyRegistry.GenericPurgePolicies.Should().HaveCount(12);
    }

    [Fact]
    public void Retention_module_governs_its_own_tables()
    {
        // Per-table coverage for the retention schema: the module that enforces retention must not
        // leave its own run-record tables ungoverned (the gap this test closes). Instantiating the
        // context reads its EF model without a database connection.
        var options = new DbContextOptionsBuilder<RetentionDbContext>()
            .UseNpgsql("Host=unused;Database=unused")
            .Options;
        using var ctx = new RetentionDbContext(options);

        var mapped = ctx.Model.GetEntityTypes()
            .Select(e => (Schema: e.GetSchema() ?? RetentionDbContext.Schema, Table: e.GetTableName()))
            .ToList();

        mapped.Should().NotBeEmpty();
        foreach (var (schema, table) in mapped)
            Policies.Should().Contain(
                p => p.Schema == schema && p.Table == table,
                "retention table {0}.{1} must have a policy", schema, table);
    }

    private static IEnumerable<Type> SafeTypes(Assembly a)
    {
        try { return a.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.OfType<Type>(); }
    }
}
