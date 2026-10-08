namespace FinanceSentry.Tests.Integration.BankSync;

using System.Data.Common;
using FinanceSentry.Core.Connections;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Infrastructure.Encryption;
using FinanceSentry.Infrastructure.Logging;
using FinanceSentry.Modules.BankSync.Application.Services;
using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Domain.Interfaces;
using FinanceSentry.Modules.BankSync.Domain.Repositories;
using FinanceSentry.Modules.BankSync.Infrastructure.Monobank;
using FinanceSentry.Modules.BankSync.Infrastructure.Persistence;
using FinanceSentry.Modules.BankSync.Infrastructure.Persistence.Repositories;
using FinanceSentry.Modules.BankSync.Infrastructure.TrueLayer;
using FinanceSentry.Tests.Integration.Shared;
using FluentAssertions;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using Xunit;

/// <summary>
/// Two sync starts for one account at the same moment (the scheduler and a manual sync) must resolve to
/// exactly one claimant; the other is refused with <c>SYNC_IN_PROGRESS</c> and never reaches the provider.
/// Each start gets its own DbContext and both are held at their first read until both have taken it, so
/// each decides on state the other has not yet written - the check-then-insert window. Real Postgres.
/// </summary>
[Trait("Category", "Integration")]
public sealed class SyncClaimConcurrencyTests : IAsyncLifetime
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly Guid _userId = Guid.NewGuid();
    private TestDatabase? _database;
    private BankAccount _account = null!;

    public async Task InitializeAsync()
    {
        _database = await PostgresServer.Postgres16.CreateDatabaseAsync();

        await using var setup = CreateContext();
        await setup.Database.EnsureCreatedAsync();

        var credential = new MonobankCredential(_userId, [1], [2], [3], 1);
        _account = new BankAccount(
            _userId, $"ext-{Guid.NewGuid():N}", "Bank", "checking", "1234", "Owner", "UAH", _userId, "monobank")
        {
            MonobankCredentialId = credential.Id,
        };
        setup.AddRange(credential, _account);
        await setup.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        if (_database is not null)
            await _database.DisposeAsync();
    }

    private BankSyncDbContext CreateContext(IInterceptor? interceptor = null)
    {
        var options = new DbContextOptionsBuilder<BankSyncDbContext>().UseNpgsql(_database!.ConnectionString);
        if (interceptor is not null)
            options.AddInterceptors(interceptor);
        return new BankSyncDbContext(options.Options, new FixedCurrentUser(null));
    }

    [DockerRequiredFact]
    public async Task Two_simultaneous_starts_for_one_account_claim_it_exactly_once()
    {
        var startGate = new StartGate(parties: 2);
        var providerEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseProvider = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var providerCalls = 0;

        var provider = new Mock<IBankProvider>();
        provider
            .Setup(p => p.SyncTransactionsAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid>(),
                It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                Interlocked.Increment(ref providerCalls);
                providerEntered.TrySetResult();
                await releaseProvider.Task;
                return ((IReadOnlyList<TransactionCandidate>)[], (DateTime?)null);
            });
        provider
            .Setup(p => p.GetAccountsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var start1 = Task.Run(() => TriggerManualSyncAsync(startGate, provider.Object));
        var start2 = Task.Run(() => TriggerManualSyncAsync(startGate, provider.Object));

        // The refused start returns while the claimant is still inside the provider. If neither start has
        // returned, both are syncing the account at once.
        var firstToReturn = Task.WhenAny(start1, start2);
        try
        {
            (await Task.WhenAny(firstToReturn, Task.Delay(Patience))).Should().BeSameAs(
                firstToReturn, "one start must be refused while the other is still syncing");

            var refused = await await firstToReturn;
            refused.Success.Should().BeFalse();
            refused.ErrorCode.Should().Be("SYNC_IN_PROGRESS");
        }
        finally
        {
            releaseProvider.TrySetResult();
        }

        var results = await Task.WhenAll(start1, start2);

        providerCalls.Should().Be(1, "only the claimant may fetch from the provider");
        results.Count(r => r.Success).Should().Be(1);
        results.Count(r => r.ErrorCode == "SYNC_IN_PROGRESS").Should().Be(1);
    }

    private async Task<SyncResult> TriggerManualSyncAsync(StartGate startGate, IBankProvider provider)
    {
        await using var context = CreateContext(new FirstReadBarrier(startGate));

        var accounts = new BankAccountRepository(context);
        var syncJobs = new SyncJobRepository(context);
        var monobankCredentials = new MonobankCredentialRepository(context);

        var transactions = new Mock<ITransactionRepository>();
        transactions
            .Setup(t => t.GetAllUniqueHashesByAccountIdUnscopedAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var encryption = new Mock<ICredentialEncryptionService>();
        encryption
            .Setup(e => e.Decrypt(It.IsAny<byte[]>(), It.IsAny<byte[]>(), It.IsAny<byte[]>(), It.IsAny<int>()))
            .Returns("token");
        var dedup = new Mock<ITransactionDeduplicationService>();
        dedup
            .Setup(d => d.FilterDuplicates(It.IsAny<IEnumerable<TransactionCandidate>>(), It.IsAny<IReadOnlySet<string>>()))
            .Returns([]);
        var providerFactory = new Mock<IBankProviderFactory>();
        providerFactory.Setup(f => f.Resolve("monobank")).Returns(provider);

        var syncService = new ScheduledSyncService(
            accounts, transactions.Object, syncJobs, encryption.Object, dedup.Object,
            Mock.Of<IBankSyncLogger>(), providerFactory.Object, monobankCredentials,
            Mock.Of<ITrueLayerConnectionRepository>(), Mock.Of<ITrueLayerClient>(),
            new MonobankBalanceCache(), Mock.Of<IAlertGeneratorService>(), Mock.Of<IUserAlertPreferencesReader>(),
            Mock.Of<IEventBus>(), Mock.Of<ITrueLayerTokenRefreshService>(), Mock.Of<IConnectionHealthShadow>());

        var coordinator = new TransactionSyncCoordinator(
            accounts, syncService, Mock.Of<IBackgroundJobClient>());

        return await coordinator.TriggerManualSyncAsync(_account.Id);
    }

    /// <summary>Releases its waiters once <c>parties</c> have arrived, or after <see cref="Patience"/>.</summary>
    private sealed class StartGate(int parties)
    {
        private readonly TaskCompletionSource _open = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrived;

        public async Task ArriveAsync()
        {
            if (Interlocked.Increment(ref _arrived) >= parties)
                _open.TrySetResult();

            await Task.WhenAny(_open.Task, Task.Delay(Patience));
        }
    }

    /// <summary>Holds the context's first command at the gate, so every start decides before any has written.</summary>
    private sealed class FirstReadBarrier(StartGate gate) : DbCommandInterceptor
    {
        private int _held;

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _held, 1) == 0)
                await gate.ArriveAsync();

            return result;
        }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _held, 1) == 0)
                await gate.ArriveAsync();

            return result;
        }
    }
}
