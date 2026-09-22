namespace FinanceSentry.Tests.Integration.Migrations;

using FinanceSentry.API.Migrations;
using FinanceSentry.Modules.Agent.Infrastructure;
using FinanceSentry.Modules.Research.Infrastructure.Persistence;
using FinanceSentry.Modules.Risk.Infrastructure.Persistence;
using FinanceSentry.Tests.Integration.Shared;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
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

    [DockerRequiredFact]
    public async Task RestartOnAMigratedDatabase_KeepsResearchDataAppliedAfterTheRiskDependency()
    {
        await using (var firstBoot = new FreshDatabaseApiFactory(_postgres!.GetConnectionString()))
        {
            firstBoot.CreateClient().Dispose();
        }

        var userId = Guid.NewGuid();
        await using var conn = new NpgsqlConnection(_postgres!.GetConnectionString());
        await conn.OpenAsync();
        await using (var insert = new NpgsqlCommand(
            """
            INSERT INTO research.asset_ledger_reads ("UserId", "Symbol", "Narrative", "SourceFingerprint")
            VALUES (@userId, 'AAPL', 'kept across restarts', 'fp')
            """,
            conn))
        {
            insert.Parameters.AddWithValue("userId", userId);
            await insert.ExecuteNonQueryAsync();
        }

        await using (var secondBoot = new FreshDatabaseApiFactory(_postgres!.GetConnectionString()))
        {
            secondBoot.CreateClient().Dispose();
        }

        await using var select = new NpgsqlCommand(
            """SELECT count(*) FROM research.asset_ledger_reads WHERE "UserId" = @userId""", conn);
        select.Parameters.AddWithValue("userId", userId);
        (await select.ExecuteScalarAsync()).Should().Be(1L,
            "a restart must not roll Research back below M014 and re-apply it onto an empty table");
    }

    /// <summary>
    /// A migration that fails during startup must stop the API from starting at all, rather than
    /// being logged and left behind while the rest of the app comes up on a half-migrated schema
    /// (#661/#664's actual bug). Reproduced here without touching any migration's content or ordering:
    /// pre-creating the table Auth's very first migration (M005_IdentitySchema) is about to create
    /// forces that migration to fail with "relation already exists", the same shape of failure Research's
    /// M012 hit against a missing table.
    /// </summary>
    [DockerRequiredFact]
    public async Task StartupMigrationFailure_HaltsStartupInsteadOfServingAHalfMigratedSchema()
    {
        var postgres = new PostgreSqlBuilder("postgres:14-alpine").Build();
        await postgres.StartAsync();
        try
        {
            await using (var conn = new NpgsqlConnection(postgres.GetConnectionString()))
            {
                await conn.OpenAsync();
                await using var collide = new NpgsqlCommand(
                    """CREATE TABLE "AspNetRoles" ("Id" text NOT NULL PRIMARY KEY)""", conn);
                await collide.ExecuteNonQueryAsync();
            }

            Action buildHost = () =>
            {
                using var factory = new FreshDatabaseApiFactory(postgres.GetConnectionString());
                using var client = factory.CreateClient();
            };

            buildHost.Should().Throw<Exception>(
                    "a migration failure must abort startup rather than let the API come up")
                .Where(ex => ContainsStartupMigrationException(ex),
                    "the halt must be traceable to the specific failed migration, not a generic crash");
        }
        finally
        {
            await postgres.DisposeAsync();
        }
    }

    /// <summary>
    /// A reachable Postgres server whose database has not been created yet must be migrated (EF's
    /// Migrate() creates the database), not mistaken for an unreachable database and skipped — which
    /// would leave the API serving with no schema at all.
    /// </summary>
    [DockerRequiredFact]
    public async Task StartupOnAReachableServerWithoutTheDatabase_CreatesAndMigratesIt()
    {
        var missingDatabase = new NpgsqlConnectionStringBuilder(_postgres!.GetConnectionString())
        {
            Database = "not_yet_created",
        }.ConnectionString;

        await using var factory = new FreshDatabaseApiFactory(missingDatabase);
        using (factory.CreateClient())
        {
            using var scope = factory.Services.CreateScope();
            var research = scope.ServiceProvider.GetRequiredService<ResearchDbContext>();

            (await research.Database.GetPendingMigrationsAsync()).Should().BeEmpty(
                "a missing database on a reachable server must be created and migrated, not skipped");
        }
    }

    /// <summary>
    /// Losing the database after earlier modules have already migrated leaves a half-migrated
    /// application, which must halt startup the same way a failed migration does. Reproduced by
    /// pointing only the last-migrated context (Agent) at an unreachable server while every earlier
    /// context migrates against the real one.
    /// </summary>
    [DockerRequiredFact]
    public void DatabaseLostAfterEarlierModulesMigrated_HaltsStartup()
    {
        Action buildHost = () =>
        {
            using var factory = new FreshDatabaseApiFactory(
                _postgres!.GetConnectionString(),
                services =>
                {
                    var toRemove = services
                        .Where(d => d.ServiceType == typeof(DbContextOptions<AgentDbContext>)
                                 || d.ServiceType == typeof(AgentDbContext)
                                 || d.ServiceType == typeof(IDbContextOptionsConfiguration<AgentDbContext>))
                        .ToList();
                    foreach (var d in toRemove)
                        services.Remove(d);

                    services.AddDbContext<AgentDbContext>(o => o.UseNpgsql(
                        "Host=127.0.0.1;Port=1;Database=unreachable;Username=test;Password=test;Timeout=1",
                        b => b.MigrationsHistoryTable("__ef_migrations_history_agent", "public")));
                });
            using var client = factory.CreateClient();
        };

        buildHost.Should().Throw<Exception>(
                "a connection lost after earlier modules migrated must abort startup rather than serve a half-migrated schema")
            .Where(ex => ContainsStartupMigrationException(ex),
                "the halt must be the named startup-migration failure, not a generic crash");
    }

    private static bool ContainsStartupMigrationException(Exception ex)
    {
        for (Exception? current = ex; current is not null; current = current.InnerException)
        {
            if (current is StartupMigrationException)
                return true;
        }

        return false;
    }

    private sealed class FreshDatabaseApiFactory(
        string connectionString, Action<IServiceCollection>? configureTestServices = null) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            if (configureTestServices is not null)
                builder.ConfigureTestServices(configureTestServices);

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
