namespace FinanceSentry.Tests.Unit.Connections;

using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Connections;
using FinanceSentry.Modules.BrokerageSync.Domain;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.Persistence;
using FinanceSentry.Modules.CryptoSync.Domain;
using FinanceSentry.Modules.CryptoSync.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

/// <summary>
/// A reconnect lifts the ActionRequired pause: the connect step resets the credential's health to its
/// not-yet-checked state, and the reset reaches the row through the same Update + SaveChanges the connect
/// handlers use. Health is not declared Healthy by the reconnect; the next scheduled run records it.
/// </summary>
public class ReconnectResetsConnectionHealthTests
{
    private static readonly DateTimeOffset FailedAt = new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);

    private static ConnectionHealth ActionRequired() =>
        new()
        {
            State = ConnectionHealthState.ActionRequired,
            ConsecutiveFailures = 3,
            FirstFailureAt = FailedAt.AddHours(-1),
            LastFailureAt = FailedAt,
            LastFailureClass = FailureClass.Credential,
            LastFailureCode = "HTTP_401",
        };

    private static DbContextOptions<T> Options<T>(string name) where T : DbContext =>
        new DbContextOptionsBuilder<T>().UseInMemoryDatabase($"{name}-{Guid.NewGuid()}").Options;

    [Fact]
    public async Task ExchangeCredential_Reconnect_ResetsHealth_AndTheResetIsPersisted()
    {
        var options = Options<CryptoSyncDbContext>("reconnect-crypto");
        var credential = ExchangeCredential.Create(Guid.NewGuid(), CryptoExchangeProvider.Binance, [1], [2], [3], [4], [5], [6], 1);
        credential.ApplyHealth(ActionRequired());
        await using (var seed = new CryptoSyncDbContext(options, NoCurrentUser.Instance))
        {
            seed.ExchangeCredentials.Add(credential);
            await seed.SaveChangesAsync();
        }

        await using (var db = new CryptoSyncDbContext(options, NoCurrentUser.Instance))
        {
            var row = await db.ExchangeCredentials.IgnoreQueryFilters([OwnerQueryFilter.Name]).SingleAsync();
            row.Health.State.Should().Be(ConnectionHealthState.ActionRequired);
            row.Reconnect([7], [8], [9], [10], [11], [12], 1);
            db.ExchangeCredentials.Update(row);
            await db.SaveChangesAsync();
        }

        await using var read = new CryptoSyncDbContext(options, NoCurrentUser.Instance);
        var reloaded = await read.ExchangeCredentials.IgnoreQueryFilters([OwnerQueryFilter.Name]).SingleAsync();
        reloaded.Health.Should().Be(new ConnectionHealth());
    }

    [Fact]
    public async Task IBKRCredential_Reactivate_ResetsHealth_AndTheResetIsPersisted()
    {
        var options = Options<BrokerageSyncDbContext>("reactivate-ibkr");
        var credential = new IBKRCredential(
            Guid.NewGuid(), "FINSENTRY", "access-token", "dh-pem",
            [1], [2], [3], [4], [5], [6], [7], [8], [9], keyVersion: 1);
        credential.ApplyHealth(ActionRequired());
        await using (var seed = new BrokerageSyncDbContext(options, NoCurrentUser.Instance))
        {
            seed.IBKRCredentials.Add(credential);
            await seed.SaveChangesAsync();
        }

        await using (var db = new BrokerageSyncDbContext(options, NoCurrentUser.Instance))
        {
            var row = await db.IBKRCredentials.IgnoreQueryFilters([OwnerQueryFilter.Name]).SingleAsync();
            row.Health.State.Should().Be(ConnectionHealthState.ActionRequired);
            row.Reactivate(
                "FINSENTRY", "new-access-token", "dh-pem", [1], [2], [3], [4], [5], [6], [7], [8], [9], keyVersion: 1);
            db.IBKRCredentials.Update(row);
            await db.SaveChangesAsync();
        }

        await using var read = new BrokerageSyncDbContext(options, NoCurrentUser.Instance);
        var reloaded = await read.IBKRCredentials.IgnoreQueryFilters([OwnerQueryFilter.Name]).SingleAsync();
        reloaded.Health.Should().Be(new ConnectionHealth());
    }

    [Fact]
    public async Task IBKRFlexCredential_Replace_ResetsHealth_AndTheResetIsPersisted()
    {
        var options = Options<BrokerageSyncDbContext>("replace-flex");
        var credential = new IBKRFlexCredential(Guid.NewGuid(), "999999", [1], [2], [3], 1);
        credential.ApplyHealth(ActionRequired());
        await using (var seed = new BrokerageSyncDbContext(options, NoCurrentUser.Instance))
        {
            seed.IBKRFlexCredentials.Add(credential);
            await seed.SaveChangesAsync();
        }

        await using (var db = new BrokerageSyncDbContext(options, NoCurrentUser.Instance))
        {
            var row = await db.IBKRFlexCredentials.IgnoreQueryFilters([OwnerQueryFilter.Name]).SingleAsync();
            row.Health.State.Should().Be(ConnectionHealthState.ActionRequired);
            row.Replace("888888", [4], [5], [6], 1);
            db.IBKRFlexCredentials.Update(row);
            await db.SaveChangesAsync();
        }

        await using var read = new BrokerageSyncDbContext(options, NoCurrentUser.Instance);
        var reloaded = await read.IBKRFlexCredentials.IgnoreQueryFilters([OwnerQueryFilter.Name]).SingleAsync();
        reloaded.Health.Should().Be(new ConnectionHealth());
    }
}
