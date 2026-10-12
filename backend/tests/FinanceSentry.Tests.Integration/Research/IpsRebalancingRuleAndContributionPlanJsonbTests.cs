namespace FinanceSentry.Tests.Integration.Research;

using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Infrastructure.Persistence;
using FinanceSentry.Tests.Integration.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

/// <summary>
/// <c>investment_policy_statements.RebalancingRule</c> and <c>ContributionPlan</c> are single-object jsonb columns
/// on a string conversion with a non-null fallback (<see cref="RebalancingRule.Default"/>, no plan). They stay
/// there: a JSON-mapped complex property reads fine on Postgres but the EF InMemory provider that the Research and
/// MCP fixtures run on cannot compile an IPS query for it (KeyNotFoundException in its shaper). This pins the
/// stored shape and the fallbacks so a later move cannot drift. Real Postgres.
/// </summary>
[Trait("Category", "Integration")]
public sealed class IpsRebalancingRuleAndContributionPlanJsonbTests : IAsyncLifetime
{
    private const string LegacyRule =
        """{"absoluteBandPct":7.5,"relativeBandPct":20,"cadence":"quarterly","contributionsFirst":false}""";
    private const string LegacyPlan = """{"amountUsd":1250.75,"cadence":"monthly"}""";

    private TestDatabase? _database;

    public async Task InitializeAsync()
    {
        _database = await PostgresServer.Postgres16.CreateDatabaseAsync();

        await using var setup = CreateContext();
        await setup.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        if (_database is not null)
            await _database.DisposeAsync();
    }

    private ResearchDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ResearchDbContext>().UseNpgsql(_database!.ConnectionString).Options,
            new FixedCurrentUser(null));

    private async Task<Guid> SeedRowAsync(string ruleJson, string? planJson)
    {
        await using var db = CreateContext();
        var ips = new InvestmentPolicyStatement { UserId = Guid.NewGuid() };
        db.PolicyStatements.Add(ips);
        await db.SaveChangesAsync();

        await db.Database.ExecuteSqlRawAsync(
            """UPDATE research.investment_policy_statements SET "RebalancingRule" = {0}::jsonb, "ContributionPlan" = NULLIF({1}, '')::jsonb WHERE "Id" = {2}""",
            ruleJson, planJson ?? string.Empty, ips.Id);
        return ips.Id;
    }

    private async Task<InvestmentPolicyStatement> ReadAsync(Guid id)
    {
        await using var db = CreateContext();
        return await db.PolicyStatements.IgnoreQueryFilters([OwnerQueryFilter.Name]).AsNoTracking()
            .SingleAsync(i => i.Id == id);
    }

    [DockerRequiredFact]
    public async Task Row_in_the_former_conversion_shape_reads_back_unchanged()
    {
        var id = await SeedRowAsync(LegacyRule, LegacyPlan);

        var stored = await ReadAsync(id);

        stored.RebalancingRule.Should().Be(new RebalancingRule(7.5m, 20m, "quarterly", false));
        stored.ContributionPlan.Should().Be(new ContributionPlan(1250.75m, "monthly"));
    }

    [DockerRequiredFact]
    public async Task Written_objects_keep_the_camelCase_json_shape()
    {
        await using var db = CreateContext();
        var ips = new InvestmentPolicyStatement
        {
            UserId = Guid.NewGuid(),
            RebalancingRule = new RebalancingRule(7.5m, 20m, "quarterly", false),
            ContributionPlan = new ContributionPlan(1250.75m, "monthly"),
        };
        db.PolicyStatements.Add(ips);
        await db.SaveChangesAsync();

        // jsonb equality: same names and values as the former conversion wrote (key order and whitespace are
        // not part of a jsonb value).
        var same = await db.Database.SqlQueryRaw<bool>(
            """
            SELECT "RebalancingRule" = {0}::jsonb AND "ContributionPlan" = NULLIF({1}, '')::jsonb AS "Value"
            FROM research.investment_policy_statements WHERE "Id" = {2}
            """,
            LegacyRule, LegacyPlan, ips.Id).SingleAsync();
        same.Should().BeTrue();
    }

    [DockerRequiredFact]
    public async Task Null_json_in_the_rule_column_reads_back_as_the_default_rule()
    {
        var id = await SeedRowAsync("null", "null");

        var stored = await ReadAsync(id);

        stored.RebalancingRule.Should().Be(RebalancingRule.Default);
        stored.ContributionPlan.Should().BeNull();
    }

    [DockerRequiredFact]
    public async Task Sql_null_contribution_plan_reads_back_as_no_plan()
    {
        var id = await SeedRowAsync(LegacyRule, null);

        var stored = await ReadAsync(id);

        stored.ContributionPlan.Should().BeNull();
        stored.RebalancingRule.Should().Be(new RebalancingRule(7.5m, 20m, "quarterly", false));
    }

    [DockerRequiredFact]
    public async Task Default_statement_round_trips_with_the_default_rule_and_no_plan()
    {
        await using var write = CreateContext();
        var ips = new InvestmentPolicyStatement { UserId = Guid.NewGuid() };
        write.PolicyStatements.Add(ips);
        await write.SaveChangesAsync();

        var stored = await ReadAsync(ips.Id);

        stored.RebalancingRule.Should().Be(RebalancingRule.Default);
        stored.ContributionPlan.Should().BeNull();
    }
}
