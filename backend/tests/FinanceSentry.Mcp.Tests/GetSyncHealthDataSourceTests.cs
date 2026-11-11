using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Connections;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Mcp.Abstractions;
using FinanceSentry.Mcp.Tools;
using FinanceSentry.Modules.BankSync.Infrastructure.Persistence;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.Persistence;
using FinanceSentry.Modules.CryptoSync.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace FinanceSentry.Mcp.Tests;

/// <summary>
/// #1375: <c>get_sync_health</c> lists the external data sources (EDGAR) beside the account providers, so a source that
/// is down is named here instead of only surfacing as empty results from the tools that read it.
/// </summary>
public sealed class GetSyncHealthDataSourceTests
{
    private const int AccountProviders = 5;

    private static readonly DateTimeOffset LastSuccess = new(2026, 11, 11, 6, 0, 0, TimeSpan.Zero);

    private static async Task<IReadOnlyList<SyncHealthEntry>> RunAsync(params IDataSourceHealthReader[] readers)
    {
        var services = new ServiceCollection();
        var key = Guid.NewGuid().ToString("N");
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Critical));
        services.AddDbContext<BankSyncDbContext>(o => o.UseInMemoryDatabase($"banksync-{key}"));
        services.AddDbContext<CryptoSyncDbContext>(o => o.UseInMemoryDatabase($"crypto-{key}"));
        services.AddDbContext<BrokerageSyncDbContext>(o => o.UseInMemoryDatabase($"brokerage-{key}"));
        services.AddSingleton<IIdentityResolver>(new FakeIdentityResolver { ResolvedUserId = Guid.NewGuid() });
        services.AddScoped<ICurrentUser, IdentityResolverCurrentUser>();
        services.Configure<ConnectionHealthOptions>(_ => { });
        foreach (var reader in readers)
        {
            services.AddSingleton<IDataSourceHealthReader>(reader);
        }

        services.AddScoped<GetSyncHealthTool>();

        await using var sp = services.BuildServiceProvider();
        await using var scope = sp.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<GetSyncHealthTool>().ExecuteAsync();
    }

    [Fact]
    public async Task WithNoDataSourceReaders_OnlyTheAccountProvidersAreListed()
    {
        var result = await RunAsync();

        result.Should().HaveCount(AccountProviders);
        result.Should().OnlyContain(e => e.Kind == null);
    }

    [Fact]
    public async Task AnUnavailableSource_IsListedAsAnError_WithItsDetail()
    {
        var result = await RunAsync(new FixedReader(new DataSourceHealth(
            "sec-edgar", DataSourceHealthState.Unavailable, null, LastSuccess, 4,
            "ticker→CIK map is unavailable (4 refreshes failed)")));

        result.Should().HaveCount(AccountProviders + 1);
        var edgar = result.Single(e => e.Provider == "sec-edgar");
        edgar.Kind.Should().Be("data_source");
        edgar.Status.Should().Be("error");
        edgar.Health.Should().Be("unavailable");
        edgar.ErrorMessage.Should().Contain("ticker→CIK map is unavailable");
        edgar.LastSyncAt.Should().BeNull();
    }

    [Fact]
    public async Task AStaleSource_IsListedAsAnError_StillShowingItsLastSuccess()
    {
        var result = await RunAsync(new FixedReader(new DataSourceHealth(
            "sec-edgar", DataSourceHealthState.Stale, LastSuccess, LastSuccess.AddDays(1), 2, "serving the copy from earlier")));

        var edgar = result.Single(e => e.Provider == "sec-edgar");
        edgar.Status.Should().Be("error");
        edgar.Health.Should().Be("stale");
        edgar.LastSyncAt.Should().Be(LastSuccess.UtcDateTime);
    }

    [Fact]
    public async Task AHealthySource_IsListedOk()
    {
        var result = await RunAsync(new FixedReader(new DataSourceHealth(
            "sec-edgar", DataSourceHealthState.Healthy, LastSuccess, null, 0, null)));

        var edgar = result.Single(e => e.Provider == "sec-edgar");
        edgar.Status.Should().Be("ok");
        edgar.Health.Should().Be("healthy");
        edgar.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task ASourceNothingHasAskedYet_IsNeverSynced_WithNoHealthLabel()
    {
        var result = await RunAsync(new FixedReader(new DataSourceHealth(
            "sec-edgar", DataSourceHealthState.NeverChecked, null, null, 0, null)));

        var edgar = result.Single(e => e.Provider == "sec-edgar");
        edgar.Status.Should().Be("never_synced");
        edgar.Health.Should().BeNull();
    }

    [Fact]
    public async Task AReaderThatThrows_DoesNotHideTheOtherEntries()
    {
        var result = await RunAsync(
            new ThrowingReader(),
            new FixedReader(new DataSourceHealth("sec-edgar", DataSourceHealthState.Healthy, LastSuccess, null, 0, null)));

        result.Should().HaveCount(AccountProviders + 1);
        result.Select(e => e.Provider).Should().Contain("sec-edgar");
    }

    private sealed class FixedReader(DataSourceHealth health) : IDataSourceHealthReader
    {
        public DataSourceHealth Read() => health;
    }

    private sealed class ThrowingReader : IDataSourceHealthReader
    {
        public DataSourceHealth Read() => throw new InvalidOperationException("reader failed");
    }
}
