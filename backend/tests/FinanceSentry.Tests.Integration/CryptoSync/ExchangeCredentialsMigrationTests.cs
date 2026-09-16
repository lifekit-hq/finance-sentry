namespace FinanceSentry.Tests.Integration.CryptoSync;

using FinanceSentry.Infrastructure.Encryption;
using FinanceSentry.Modules.CryptoSync.Domain;
using FinanceSentry.Modules.CryptoSync.Infrastructure.Encryption;
using FinanceSentry.Modules.CryptoSync.Infrastructure.Persistence;
using FinanceSentry.Tests.Integration.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

/// <summary>
/// M004 (#472) and key rotation (#493) against a real PostgreSQL, from the pre-#472 schema up:
/// production's Binance credential and holdings must survive the move to the provider-keyed
/// tables byte for byte, and the rotation that retires the disclosed key must then move that row
/// from v1 to v2 with the v1 key supplied explicitly as the old key — the deployment #493 needs.
///
/// Requires Docker (<see cref="DockerRequiredFactAttribute"/> skips otherwise; CI has it).
/// </summary>
[Trait("Category", "Integration")]
public sealed class ExchangeCredentialsMigrationTests : IAsyncLifetime
{
    private const string PreviousMigration = "20260628000000_M003_AddCostBasisColumns";
    private const string FreshKeyV2 = "ZmVkY2JhOTg3NjU0MzIxMGZlZGNiYTk4NzY1NDMyMTA=";
    private const string ApiKey = "binance-api-key-written-before-472";
    private const string ApiSecret = "binance-api-secret-written-before-472";

    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _credentialId = Guid.NewGuid();
    private PostgreSqlContainer? _postgres;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:14-alpine")
            .Build();
        await _postgres.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_postgres is not null)
            await _postgres.DisposeAsync();
    }

    private CryptoSyncDbContext NewContext() =>
        new(new DbContextOptionsBuilder<CryptoSyncDbContext>()
            .UseNpgsql(_postgres!.GetConnectionString(), b => b.MigrationsHistoryTable("__EFMigrationsHistory", "public"))
            .Options);

    private static CredentialEncryptionService Encryption(int currentVersion, Dictionary<int, string> keys) =>
        new(Options.Create(new EncryptionOptions { CurrentKeyVersion = currentVersion, Keys = keys }));

    /// <summary>How production wrote it: the pre-#472 schema, encrypted under the disclosed key.</summary>
    private async Task<(byte[] Ciphertext, byte[] Iv, byte[] Tag)> SeedPre472StateAsync()
    {
        await using (var ctx = NewContext())
        {
            await ctx.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        }

        var disclosed = Encryption(1, new() { [1] = CredentialEncryptionService.DisclosedFallbackKeyBase64 });
        var key = disclosed.Encrypt(ApiKey);
        var secret = disclosed.Encrypt(ApiSecret);

        await using var conn = new NpgsqlConnection(_postgres!.GetConnectionString());
        await conn.OpenAsync();
        await using (var insert = new NpgsqlCommand("""
            INSERT INTO crypto_sync."BinanceCredentials"
                ("Id", "UserId", "EncryptedApiKey", "ApiKeyIv", "ApiKeyAuthTag",
                 "EncryptedApiSecret", "ApiSecretIv", "ApiSecretAuthTag",
                 "KeyVersion", "IsActive", "LastSyncAt", "LastSyncError", "CreatedAt")
            VALUES (@id, @user, @k, @kiv, @ktag, @s, @siv, @stag, 1, TRUE, now(), NULL, now());
            INSERT INTO crypto_sync."CryptoHoldings"
                ("Id", "UserId", "Asset", "FreeQuantity", "LockedQuantity", "UsdValue", "SyncedAt",
                 "LastTradeId", "TradeCount")
            VALUES (gen_random_uuid(), @user, 'BTC', 0.5, 0, 30000, now(), 41, 3),
                   (gen_random_uuid(), @user, 'ETH', 2, 0, 6000, now(), 0, 0);
            """, conn))
        {
            insert.Parameters.AddWithValue("id", _credentialId);
            insert.Parameters.AddWithValue("user", _userId);
            insert.Parameters.AddWithValue("k", key.Ciphertext);
            insert.Parameters.AddWithValue("kiv", key.Iv);
            insert.Parameters.AddWithValue("ktag", key.AuthTag);
            insert.Parameters.AddWithValue("s", secret.Ciphertext);
            insert.Parameters.AddWithValue("siv", secret.Iv);
            insert.Parameters.AddWithValue("stag", secret.AuthTag);
            await insert.ExecuteNonQueryAsync();
        }

        return (key.Ciphertext, key.Iv, key.AuthTag);
    }

    [DockerRequiredFact]
    public async Task M004_MovesTheBinanceCredentialAcrossUnchanged_AndBackfillsTheHoldings()
    {
        var (ciphertext, iv, tag) = await SeedPre472StateAsync();

        await using (var ctx = NewContext())
        {
            await ctx.Database.MigrateAsync();
        }

        await using var read = NewContext();
        var credential = await read.ExchangeCredentials.AsNoTracking().SingleAsync();
        credential.Id.Should().Be(_credentialId);
        credential.UserId.Should().Be(_userId);
        credential.Provider.Should().Be(CryptoExchangeProvider.Binance);
        credential.KeyVersion.Should().Be(1);
        credential.IsActive.Should().BeTrue();
        credential.EncryptedApiKey.Should().Equal(ciphertext);
        credential.ApiKeyIv.Should().Equal(iv);
        credential.ApiKeyAuthTag.Should().Equal(tag);

        var holdings = await read.CryptoHoldings.AsNoTracking().OrderBy(h => h.Asset).ToListAsync();
        holdings.Should().OnlyContain(h => h.Provider == CryptoExchangeProvider.Binance);
        holdings.Select(h => (h.Asset, h.TradeCursor, h.TradeCount)).Should().Equal(
            ("BTC", "42", 3),
            ("ETH", (string?)null, 0));
    }

    [DockerRequiredFact]
    public async Task M004_SameAssetOnTwoVenues_IsAllowed_ButNotTwiceOnOne()
    {
        await SeedPre472StateAsync();
        await using (var ctx = NewContext())
        {
            await ctx.Database.MigrateAsync();
        }

        await using (var ctx = NewContext())
        {
            ctx.CryptoHoldings.Add(CryptoHolding.Create(_userId, CryptoExchangeProvider.RevolutX, "BTC", 0.1m, 0m, 6_000m));
            ctx.ExchangeCredentials.Add(ExchangeCredential.Create(
                _userId, CryptoExchangeProvider.RevolutX, [1], [2], [3], [4], [5], [6], 1));
            await ctx.Invoking(c => c.SaveChangesAsync()).Should().NotThrowAsync();
        }

        await using (var ctx = NewContext())
        {
            ctx.CryptoHoldings.Add(CryptoHolding.Create(_userId, CryptoExchangeProvider.Binance, "BTC", 1m, 0m, 1m));
            await ctx.Invoking(c => c.SaveChangesAsync()).Should().ThrowAsync<DbUpdateException>();
        }

        await using (var ctx = NewContext())
        {
            ctx.ExchangeCredentials.Add(ExchangeCredential.Create(
                _userId, CryptoExchangeProvider.Binance, [1], [2], [3], [4], [5], [6], 1));
            await ctx.Invoking(c => c.SaveChangesAsync()).Should().ThrowAsync<DbUpdateException>();
        }
    }

    [DockerRequiredFact]
    public async Task Rotation_WithTheDisclosedKeyAsTheOldVersion_MovesTheMigratedRowToV2()
    {
        await SeedPre472StateAsync();
        await using (var ctx = NewContext())
        {
            await ctx.Database.MigrateAsync();
        }

        // The production configuration #493 asks for: the old key kept at v1 purely to read the
        // existing rows, a fresh key at v2 that everything is written under.
        var options = new EncryptionOptions
        {
            CurrentKeyVersion = 2,
            Keys = new()
            {
                [1] = CredentialEncryptionService.DisclosedFallbackKeyBase64,
                [2] = FreshKeyV2,
            },
        };
        new EncryptionOptionsValidator(new ProductionEnvironment(), NullLogger<EncryptionOptionsValidator>.Instance)
            .Validate(name: null, options)
            .Succeeded.Should().BeTrue("the disclosed key is allowed as the OLD version, only never as the current one");

        await using (var ctx = NewContext())
        {
            var service = new CredentialEncryptionService(Options.Create(options));
            var rotation = new CredentialKeyRotationService(
                [new ExchangeCredentialRotationTarget(ctx, service)],
                Options.Create(options),
                NullLogger<CredentialKeyRotationService>.Instance);

            var migrated = await rotation.RotateAllAsync();

            migrated.Should().Equal(new Dictionary<string, int> { ["ExchangeCredentials"] = 1 });
            (await rotation.RotateAllAsync())["ExchangeCredentials"].Should().Be(0, "a second boot is a no-op");
        }

        // Readable with v2 ALONE: the disclosed key can now be removed from configuration.
        var v2Only = Encryption(2, new() { [2] = FreshKeyV2 });
        await using var read = NewContext();
        var row = await read.ExchangeCredentials.AsNoTracking().SingleAsync();
        row.KeyVersion.Should().Be(2);
        v2Only.Decrypt(row.EncryptedApiKey, row.ApiKeyIv, row.ApiKeyAuthTag, row.KeyVersion).Should().Be(ApiKey);
        v2Only.Decrypt(row.EncryptedApiSecret, row.ApiSecretIv, row.ApiSecretAuthTag, row.KeyVersion).Should().Be(ApiSecret);
    }

    [DockerRequiredFact]
    public async Task M004_Down_RestoresTheBinanceTable_AndDropsOtherVenues()
    {
        var (ciphertext, _, _) = await SeedPre472StateAsync();
        await using (var ctx = NewContext())
        {
            await ctx.Database.MigrateAsync();
            ctx.CryptoHoldings.Add(CryptoHolding.Create(_userId, CryptoExchangeProvider.RevolutX, "BTC", 0.1m, 0m, 6_000m));
            await ctx.SaveChangesAsync();
            await ctx.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        }

        await using var conn = new NpgsqlConnection(_postgres!.GetConnectionString());
        await conn.OpenAsync();
        await using var credential = new NpgsqlCommand(
            """SELECT "EncryptedApiKey" FROM crypto_sync."BinanceCredentials" WHERE "Id" = @id""", conn);
        credential.Parameters.AddWithValue("id", _credentialId);
        ((byte[])(await credential.ExecuteScalarAsync())!).Should().Equal(ciphertext);

        await using var holdings = new NpgsqlCommand(
            """SELECT string_agg("Asset" || ':' || "LastTradeId", ',' ORDER BY "Asset") FROM crypto_sync."CryptoHoldings" """,
            conn);
        ((string)(await holdings.ExecuteScalarAsync())!).Should().Be("BTC:41,ETH:0");
    }

    private sealed class ProductionEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "FinanceSentry.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
