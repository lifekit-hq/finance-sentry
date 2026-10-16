using System.Text.Json;
using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Connections;
using FinanceSentry.Mcp.Abstractions;
using FinanceSentry.Mcp.Tools;
using FinanceSentry.Modules.BankSync.Application.Connections;
using FinanceSentry.Modules.BankSync.Domain.Repositories;
using FinanceSentry.Modules.BankSync.Infrastructure.Persistence;
using FinanceSentry.Modules.BankSync.Infrastructure.Persistence.Repositories;
using FinanceSentry.Modules.BrokerageSync.Domain;
using FinanceSentry.Modules.BrokerageSync.Domain.Repositories;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.Connections;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.Persistence;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.Persistence.Repositories;
using FinanceSentry.Modules.CryptoSync.Domain.Repositories;
using FinanceSentry.Modules.CryptoSync.Infrastructure.Connections;
using FinanceSentry.Modules.CryptoSync.Infrastructure.Persistence;
using FinanceSentry.Modules.CryptoSync.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace FinanceSentry.Mcp.Tests;

/// <summary>
/// <c>get_sync_health</c> reads a policy-driven provider from the connection health its module stores (slice S5), through
/// every module's <see cref="IConnectionHealthReader"/>, and labels it as the app's Accounts page does; a shadow provider's
/// entry stays exactly what its own columns give, with no health field.
/// </summary>
public sealed class GetSyncHealthConnectionHealthTests
{
    private readonly Guid _userId = Guid.NewGuid();

    private ServiceProvider BuildProvider(params string[] activeProviders)
    {
        var services = new ServiceCollection();
        var key = Guid.NewGuid().ToString("N");
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddDbContext<BankSyncDbContext>(o => o.UseInMemoryDatabase($"banksync-{key}"));
        services.AddDbContext<CryptoSyncDbContext>(o => o.UseInMemoryDatabase($"crypto-{key}"));
        services.AddDbContext<BrokerageSyncDbContext>(o => o.UseInMemoryDatabase($"brokerage-{key}"));
        services.AddSingleton<IIdentityResolver>(new FakeIdentityResolver { ResolvedUserId = _userId });
        services.AddScoped<ICurrentUser, IdentityResolverCurrentUser>();

        services.AddScoped<IMonobankCredentialRepository, MonobankCredentialRepository>();
        services.AddScoped<ITrueLayerConnectionRepository, TrueLayerConnectionRepository>();
        services.AddScoped<IBankAccountRepository, BankAccountRepository>();
        services.AddScoped<IIBKRCredentialRepository, IBKRCredentialRepository>();
        services.AddScoped<IIBKRFlexCredentialRepository, IBKRFlexCredentialRepository>();
        services.AddScoped<IExchangeCredentialRepository, ExchangeCredentialRepository>();
        services.AddScoped<IConnectionHealthReader, BankConnectionHealthReader>();
        services.AddScoped<IConnectionHealthReader, BrokerageConnectionHealthReader>();
        services.AddScoped<IConnectionHealthReader, CryptoConnectionHealthReader>();
        services.Configure<ConnectionHealthOptions>(o => o.ActiveProviders = [.. activeProviders]);
        services.AddScoped<GetSyncHealthTool>();
        return services.BuildServiceProvider();
    }

    private async Task<SyncHealthEntry> IbkrEntryAsync(ConnectionHealthState state, params string[] activeProviders)
    {
        await using var sp = BuildProvider(activeProviders);
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BrokerageSyncDbContext>();
        var credential = new IBKRCredential(
            _userId, "consumer", "token", "dh", [1], [2], [3], [4], [5], [6], [7], [8], [9], 1);
        credential.RecordSyncSuccess();
        credential.RecordSyncError("BrokerAuthException");
        credential.ApplyHealth(new ConnectionHealth { State = state, LastFailureCode = "HTTP_401" });
        db.IBKRCredentials.Add(credential);
        await db.SaveChangesAsync();

        var result = await scope.ServiceProvider.GetRequiredService<GetSyncHealthTool>().ExecuteAsync();
        return result.Single(e => e.Provider == "ibkr");
    }

    [Theory]
    [InlineData(ConnectionHealthState.ActionRequired, "error", "reconnect_needed")]
    [InlineData(ConnectionHealthState.Failing, "error", "retrying")]
    [InlineData(ConnectionHealthState.Dormant, "error", "dormant")]
    [InlineData(ConnectionHealthState.Degraded, "ok", "healthy")]
    [InlineData(ConnectionHealthState.Healthy, "ok", "healthy")]
    public async Task A_policy_driven_provider_is_read_from_its_connection_health(
        ConnectionHealthState state, string status, string health)
    {
        var entry = await IbkrEntryAsync(state, "ibkr");

        entry.Status.Should().Be(status);
        entry.Health.Should().Be(health);
    }

    [Theory]
    [InlineData(ConnectionHealthState.ActionRequired, "error", "reconnect_needed")]
    [InlineData(ConnectionHealthState.Dormant, "error", "dormant")]
    public async Task A_listed_Flex_credentials_health_shows_under_the_ibkr_entry(
        ConnectionHealthState state, string status, string health)
    {
        await using var sp = BuildProvider("ibkr", "ibkr-flex");
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BrokerageSyncDbContext>();
        var flex = new IBKRFlexCredential(_userId, "query", [1], [2], [3], 1);
        flex.ApplyHealth(new ConnectionHealth { State = state, LastFailureCode = "HTTP_401" });
        db.IBKRFlexCredentials.Add(flex);
        await db.SaveChangesAsync();

        var entry = (await scope.ServiceProvider.GetRequiredService<GetSyncHealthTool>().ExecuteAsync())
            .Single(e => e.Provider == "ibkr");

        entry.Status.Should().Be(status);
        entry.Health.Should().Be(health);
    }

    [Theory]
    [InlineData(ConnectionHealthState.Dormant, "error", "dormant")]
    [InlineData(ConnectionHealthState.Failing, "error", "retrying")]
    public async Task A_Degraded_connection_does_not_hide_an_accounts_own_state(
        ConnectionHealthState accountState, string status, string health)
    {
        await using var sp = BuildProvider("monobank");
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BankSyncDbContext>();
        var token = new FinanceSentry.Modules.BankSync.Domain.MonobankCredential(_userId, [1], [2], [3], 1);
        token.ApplyHealth(new ConnectionHealth { State = ConnectionHealthState.Degraded });
        var account = new FinanceSentry.Modules.BankSync.Domain.BankAccount(
            _userId, "ext-1", "Monobank", "checking", "1111", "Owner", "UAH", _userId, "monobank")
        {
            MonobankCredentialId = token.Id,
        };
        account.ApplyHealth(new ConnectionHealth { State = accountState });
        db.MonobankCredentials.Add(token);
        db.BankAccounts.Add(account);
        await db.SaveChangesAsync();

        var entry = (await scope.ServiceProvider.GetRequiredService<GetSyncHealthTool>().ExecuteAsync())
            .Single(e => e.Provider == "monobank");

        entry.Status.Should().Be(status);
        entry.Health.Should().Be(health);
    }

    [Fact]
    public async Task A_connection_with_no_accounts_that_needs_a_reconnect_shows_beside_a_healthy_one_with_accounts()
    {
        await using var sp = BuildProvider("truelayer");
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BankSyncDbContext>();
        var expired = new FinanceSentry.Modules.BankSync.Domain.TrueLayerConnection(_userId, "bank-a", "Bank A", "ref-a");
        expired.ApplyHealth(new ConnectionHealth { State = ConnectionHealthState.ActionRequired, LastFailureCode = "CONSENT_EXPIRED" });
        var working = new FinanceSentry.Modules.BankSync.Domain.TrueLayerConnection(_userId, "bank-b", "Bank B", "ref-b");
        var account = new FinanceSentry.Modules.BankSync.Domain.BankAccount(
            _userId, "ext-2", "Bank B", "checking", "2222", "Owner", "EUR", _userId, "truelayer")
        {
            TrueLayerConnectionId = working.Id,
        };
        db.TrueLayerConnections.AddRange(expired, working);
        db.BankAccounts.Add(account);
        await db.SaveChangesAsync();

        var entry = (await scope.ServiceProvider.GetRequiredService<GetSyncHealthTool>().ExecuteAsync())
            .Single(e => e.Provider == "truelayer");

        entry.Status.Should().Be("error");
        entry.Health.Should().Be("reconnect_needed");
    }

    [Fact]
    public async Task A_shadow_Flex_credentials_health_does_not_show_under_the_ibkr_entry()
    {
        await using var sp = BuildProvider("ibkr");
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BrokerageSyncDbContext>();
        var flex = new IBKRFlexCredential(_userId, "query", [1], [2], [3], 1);
        flex.ApplyHealth(new ConnectionHealth { State = ConnectionHealthState.ActionRequired, LastFailureCode = "HTTP_401" });
        db.IBKRFlexCredentials.Add(flex);
        await db.SaveChangesAsync();

        var entry = (await scope.ServiceProvider.GetRequiredService<GetSyncHealthTool>().ExecuteAsync())
            .Single(e => e.Provider == "ibkr");

        entry.Health.Should().BeNull("nothing of an unlisted Flex row counts, and no IBKR row exists to label");
    }

    [Fact]
    public async Task A_confirmed_credential_loss_says_a_reconnect_is_needed()
    {
        var entry = await IbkrEntryAsync(ConnectionHealthState.ActionRequired, "ibkr");

        entry.ErrorMessage.Should().Be("Reconnection required.");
    }

    [Fact]
    public async Task A_failure_the_user_was_not_told_about_carries_no_error_message()
    {
        var entry = await IbkrEntryAsync(ConnectionHealthState.Degraded, "ibkr");

        entry.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task A_shadow_provider_keeps_its_own_columns_and_carries_no_health_field()
    {
        var entry = await IbkrEntryAsync(ConnectionHealthState.Degraded, "monobank");

        entry.Status.Should().Be("error", "its own column still records the last error, as before");
        entry.ErrorMessage.Should().Be("BrokerAuthException");
        entry.Health.Should().BeNull();
        JsonSerializer.Serialize(entry).Should().NotContain("Health", "a shadow entry serialises exactly as before");
    }

    [Fact]
    public async Task With_the_switch_empty_every_entry_is_as_before()
    {
        var entry = await IbkrEntryAsync(ConnectionHealthState.ActionRequired);

        entry.Status.Should().Be("error");
        entry.ErrorMessage.Should().Be("BrokerAuthException");
        entry.Health.Should().BeNull();
    }
}
