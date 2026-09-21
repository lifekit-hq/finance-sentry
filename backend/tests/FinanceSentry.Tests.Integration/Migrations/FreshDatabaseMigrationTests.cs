namespace FinanceSentry.Tests.Integration.Migrations;

using FinanceSentry.Modules.Research.Infrastructure.Persistence;
using FinanceSentry.Modules.Risk.Infrastructure.Persistence;
using FinanceSentry.Tests.Integration.Shared;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

/// <summary>
/// A brand-new database, migrated the ordinary way: the API's own startup path
/// (<c>Program.cs</c> -&gt; <c>MigrateAllModules</c>), against an empty container database, with no
/// hand-sequenced migrations. This is exactly the path a rebuild or disaster-recovery restore takes.
///
/// Reproduces the fresh-database failure (#661): Research's M012 reconciles data into
/// <c>risk.risk_rule_sets</c>, a table only Risk's own migrations create. On a genuinely empty
/// database, Research used to migrate before Risk, so M012 threw "relation risk.risk_rule_sets does
/// not exist" — and because a failed module's migration is caught and logged rather than stopping
/// startup, the API then came up with Research's schema stuck before M012, missing M013's
/// <c>EntryPrice</c> column on <c>theses</c>, and thesis saves failed until an operator noticed and
/// restarted. This test fails on the pre-fix migration order and passes once Risk's schema-creating
/// migration is guaranteed to run before Research's M012.
///
/// Requires Docker (<see cref="DockerRequiredFactAttribute"/> skips otherwise; CI has it).
/// </summary>
[Trait("Category", "Integration")]
public sealed class FreshDatabaseMigrationTests : IAsyncLifetime
{
    private PostgreSqlContainer? _postgres;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder("postgres:14-alpine").Build();
        await _postgres.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_postgres is not null)
            await _postgres.DisposeAsync();
    }

    [DockerRequiredFact]
    public async Task StartupMigratesAFreshDatabase_ResearchAndRiskBothFinishCleanly()
    {
        await using var factory = new FreshDatabaseApiFactory(_postgres!.GetConnectionString());

        // Building the factory's TestServer runs Program.cs's ordinary startup path — including the
        // unmodified app.MigrateAllModules() call — against the freshly-started, empty database.
        using (factory.CreateClient())
        {
            using var scope = factory.Services.CreateScope();

            var research = scope.ServiceProvider.GetRequiredService<ResearchDbContext>();
            var risk = scope.ServiceProvider.GetRequiredService<RiskDbContext>();

            (await research.Database.GetPendingMigrationsAsync()).Should().BeEmpty(
                "startup must finish migrating Research to the latest migration, not stop part-way " +
                "through M012 because risk.risk_rule_sets did not exist yet");
            (await risk.Database.GetPendingMigrationsAsync()).Should().BeEmpty(
                "Research's M012 depends on Risk's schema, so Risk must migrate cleanly too");
        }

        // The concrete production symptom: M013 (EntryPrice) never applied because M012 threw
        // before it, so thesis saves had no column to write EntryPrice into.
        await using var conn = new NpgsqlConnection(_postgres!.GetConnectionString());
        await conn.OpenAsync();
        await using var column = new NpgsqlCommand(
            """
            SELECT count(*) FROM information_schema.columns
            WHERE table_schema = 'research' AND table_name = 'theses' AND column_name = 'EntryPrice'
            """,
            conn);
        (await column.ExecuteScalarAsync()).Should().Be(1L,
            "M013_ThesisEntryPrice must have applied so theses.EntryPrice exists");
    }

    private sealed class FreshDatabaseApiFactory(string connectionString) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Default", connectionString);
            builder.UseSetting("ConnectionStrings:ReadOnly", connectionString);
            builder.UseSetting("Deduplication:MasterKeyBase64",
                "dGVzdC1vbmx5LWtleS1ub3QtdGhlLWxlYWtlZC1vbmU=");
            builder.UseSetting("Encryption:CurrentKeyVersion", "1");
            builder.UseSetting("Encryption:Keys:1",
                "dGVzdC1vbmx5LWtleS1ub3QtdGhlLWxlYWtlZC1vbmU=");
            builder.UseSetting("Jwt:Secret",
                "test-jwt-secret-key-for-integration-tests-minimum-32-chars");
            builder.UseSetting("Jwt:ExpiryMinutes", "60");
            builder.UseSetting("GoogleOAuth:ClientId", "test-client-id");
        }
    }
}
