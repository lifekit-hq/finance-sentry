namespace FinanceSentry.Tests.Integration.Migrations;

using System.Net;
using System.Net.Sockets;
using System.Text.Json;
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

    /// <summary>
    /// The case the halt work deliberately left non-fatal: the database is unreachable when the API
    /// starts, so startup skips every module's migrations and the API comes up anyway. When the database
    /// then returns, the <c>database</c> readiness check goes Healthy while the schema is still whatever
    /// the database has — here, completely empty. Readiness must then say so: the <c>migrations</c>
    /// check stays Unhealthy, names the pending migrations, and tells the operator to restart, so the
    /// cause is visible at /api/v1/health/ready rather than only as unrelated 500s from request handlers.
    /// </summary>
    [DockerRequiredFact]
    public async Task DatabaseUnreachableAtStartupThenReachable_ReadinessNamesTheSkippedMigrations()
    {
        // Reserve a host port now, boot the API against it while nothing listens, then start Postgres
        // bound to that same port so the very connection string startup could not reach becomes live.
        var port = ReserveFreeTcpPort();
        var connectionString = new NpgsqlConnectionStringBuilder
        {
            Host = "127.0.0.1",
            Port = port,
            Database = "finance_sentry",
            Username = "postgres",
            Password = "postgres",
            Timeout = 2,
        }.ConnectionString;

        await using var factory = new FreshDatabaseApiFactory(connectionString);
        using var client = factory.CreateClient();

        var status = factory.Services.GetRequiredService<StartupMigrationStatus>();
        status.MigrationsSkipped.Should().BeTrue(
            "an unreachable database must not stop startup, but the skip must be recorded");

        var whileUnreachable = await ReadReadiness(client);
        whileUnreachable.Status.Should().Be(HttpStatusCode.ServiceUnavailable);
        whileUnreachable.CheckStatus("database").Should().Be("Unhealthy");
        whileUnreachable.CheckStatus("migrations").Should().Be("Unhealthy");

        var postgres = new PostgreSqlBuilder("postgres:14-alpine")
            .WithDatabase("finance_sentry")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .WithPortBinding(port, PostgreSqlBuilder.PostgreSqlPort)
            .Build();
        await postgres.StartAsync();
        try
        {
            var nowReachable = await ReadReadiness(client);

            nowReachable.CheckStatus("database").Should().Be("Healthy",
                "the database is back, and that check alone would now make the API look ready");
            nowReachable.Status.Should().Be(HttpStatusCode.ServiceUnavailable,
                "readiness must still fail: this process never migrated the schema it is serving on");
            nowReachable.CheckStatus("migrations").Should().Be("Unhealthy");

            var description = nowReachable.CheckDescription("migrations");
            description.Should().Contain("skipped at startup");
            description.Should().Contain("Restart the API");
            description.Should().Contain("pending migrations", "the schema is behind and readiness must say so");
            description.Should().Contain(nameof(ResearchDbContext), "each module behind is named");
            description.Should().Contain("M013", "the specific migration the original 500 was missing is listed");

            var researchPending = await ResearchPendingMigrationsCount(factory);
            researchPending.Should().BeGreaterThan(0,
                "nothing migrated: the health check reports, it does not migrate on the operator's behalf");
        }
        finally
        {
            await postgres.DisposeAsync();
        }
    }

    private static int ReserveFreeTcpPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static async Task<int> ResearchPendingMigrationsCount(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var research = scope.ServiceProvider.GetRequiredService<ResearchDbContext>();
        return (await research.Database.GetPendingMigrationsAsync()).Count();
    }

    private static async Task<ReadinessReport> ReadReadiness(HttpClient client)
    {
        var response = await client.GetAsync("/api/v1/health/ready");
        var json = await response.Content.ReadAsStringAsync();
        return new ReadinessReport(response.StatusCode, JsonDocument.Parse(json));
    }

    private sealed record ReadinessReport(HttpStatusCode Status, JsonDocument Body)
    {
        public string? CheckStatus(string name) => Check(name).GetProperty("status").GetString();

        public string? CheckDescription(string name) =>
            Check(name).TryGetProperty("description", out var description) ? description.GetString() : null;

        private JsonElement Check(string name) => Body.RootElement.GetProperty("checks").EnumerateArray()
            .Single(check => check.GetProperty("name").GetString() == name);
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
