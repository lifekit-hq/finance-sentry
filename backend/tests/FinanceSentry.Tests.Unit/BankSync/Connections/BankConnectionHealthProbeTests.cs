namespace FinanceSentry.Tests.Unit.BankSync.Connections;

using FinanceSentry.Core.Connections;
using FinanceSentry.Infrastructure.Encryption;
using FinanceSentry.Modules.BankSync.Application.Connections;
using FinanceSentry.Modules.BankSync.Application.Services;
using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Domain.Interfaces;
using FinanceSentry.Modules.BankSync.Domain.Repositories;
using FinanceSentry.Modules.BankSync.Infrastructure.Monobank;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

/// <summary>
/// The bank probes (slice S4): Monobank's client-info and TrueLayer's token refresh, for suspect connections only,
/// with Monobank's one-call-a-minute limit respected through its client-info cache.
/// </summary>
public class BankConnectionHealthProbeTests
{
    private const string Token = "mono-token";

    private static readonly DateTimeOffset Now = new(2026, 10, 15, 8, 0, 0, TimeSpan.Zero);

    private readonly Mock<IMonobankCredentialRepository> _monobank = new();
    private readonly Mock<ITrueLayerConnectionRepository> _trueLayer = new();
    private readonly Mock<IMonobankAdapter> _monobankAdapter = new();
    private readonly Mock<ITrueLayerTokenRefreshService> _tokenRefresh = new();
    private readonly Mock<ICredentialEncryptionService> _encryption = new();
    private readonly Mock<IBankConnectionHealth> _health = new();
    private readonly MonobankBalanceCache _cache = new(new FakeTimeProvider(Now));
    private readonly MonobankCredential _credential = new(Guid.NewGuid(), [1], [2], [3], 1);
    private readonly TrueLayerConnection _connection = new(Guid.NewGuid(), "mock-bank", "Mock Bank", "ref-1");

    public BankConnectionHealthProbeTests()
    {
        _encryption.Setup(e => e.Decrypt(It.IsAny<byte[]>(), It.IsAny<byte[]>(), It.IsAny<byte[]>(), It.IsAny<int>())).Returns(Token);
        _monobank.Setup(r => r.GetByIdUnscopedAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(_credential);
        _trueLayer.Setup(r => r.GetByIdUnscopedAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(_connection);
        _monobankAdapter
            .Setup(a => a.GetClientInfoAsync(Token, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MonobankClientInfo("c1", "Client", [new MonobankAccountInfo("acc-1", "Black", "black", "**1234", 980, 100, 0)]));
    }

    private BankConnectionHealthProbe CreateProbe() =>
        new(
            _monobank.Object,
            _trueLayer.Object,
            _monobankAdapter.Object,
            _cache,
            _tokenRefresh.Object,
            _encryption.Object,
            _health.Object,
            NullLogger<BankConnectionHealthProbe>.Instance);

    private static ConnectionHealthProbeTarget Target(string provider) =>
        new(new ConnectionHealthSubject(provider, "Connection", Guid.NewGuid(), Guid.NewGuid()), new ConnectionHealth(), ProbesDormant: false);

    private static ConnectionHealth Suspect() => new() { State = ConnectionHealthState.Degraded, SuspectSince = Now };

    [Fact]
    public async Task Candidates_AreTheSuspectConnectionsOnly()
    {
        var suspect = new MonobankCredential(Guid.NewGuid(), [1], [2], [3], 1);
        suspect.ApplyHealth(Suspect());
        var dormant = new MonobankCredential(Guid.NewGuid(), [1], [2], [3], 1);
        dormant.ApplyHealth(new ConnectionHealth { State = ConnectionHealthState.Dormant, LastFailureAt = Now });
        _monobank.Setup(r => r.GetAllUnscopedAsync(It.IsAny<CancellationToken>())).ReturnsAsync([suspect, dormant, new MonobankCredential()]);
        _connection.ApplyHealth(Suspect());
        _trueLayer.Setup(r => r.GetAllLinkedUnscopedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([_connection, new TrueLayerConnection(Guid.NewGuid(), "other", "Other", "ref-2")]);

        var candidates = await CreateProbe().ListCandidatesAsync();

        candidates.Select(c => c.Subject.Provider).Should().BeEquivalentTo(
            [MonobankFailureClassifier.ProviderName, TrueLayerFailureClassifier.ProviderName]);
        candidates.Should().OnlyContain(c => !c.ProbesDormant);
    }

    [Fact]
    public async Task Monobank_ClientInfoAnswers_RecordsASuccess_AndFillsTheCache()
    {
        await CreateProbe().ProbeAsync(Target(MonobankFailureClassifier.ProviderName), ConnectionHealthProbeReason.Suspect);

        _health.Verify(h => h.RecordConnectionSuccessAsync(
            It.Is<BankConnectionRef>(c => c.Provider == MonobankFailureClassifier.ProviderName), It.IsAny<CancellationToken>()), Times.Once);
        _cache.HasFreshEntries(Token).Should().BeTrue();
    }

    [Fact]
    public async Task Monobank_ClientInfoFetchedWithinTheMinute_IsNotCalledAgain()
    {
        _cache.Set(Token, "acc-1", new BankAccountInfo("acc-1", "Black", "black", "1234", 1m, "UAH", "Client"));

        await CreateProbe().ProbeAsync(Target(MonobankFailureClassifier.ProviderName), ConnectionHealthProbeReason.Suspect);

        _monobankAdapter.Verify(a => a.GetClientInfoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _health.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Monobank_ClientInfoRefused_RecordsAProbeFailure()
    {
        var refused = new HttpRequestException("Unauthorized");
        _monobankAdapter.Setup(a => a.GetClientInfoAsync(Token, It.IsAny<CancellationToken>())).ThrowsAsync(refused);

        await CreateProbe().ProbeAsync(Target(MonobankFailureClassifier.ProviderName), ConnectionHealthProbeReason.Suspect);

        _health.Verify(h => h.RecordConnectionProbeFailureAsync(
            It.Is<BankConnectionRef>(c => c.Provider == MonobankFailureClassifier.ProviderName), refused, It.IsAny<CancellationToken>()),
            Times.Once);
        _health.Verify(h => h.RecordConnectionSuccessAsync(It.IsAny<BankConnectionRef>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TrueLayer_TokenRefreshes_RecordsASuccess()
    {
        _tokenRefresh.Setup(t => t.AcquireAccessTokenAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync("access");

        await CreateProbe().ProbeAsync(Target(TrueLayerFailureClassifier.ProviderName), ConnectionHealthProbeReason.Suspect);

        _health.Verify(h => h.RecordConnectionSuccessAsync(
            It.Is<BankConnectionRef>(c => c.Provider == TrueLayerFailureClassifier.ProviderName), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TrueLayer_TokenRefreshRefused_RecordsAProbeFailure()
    {
        var refused = new InvalidOperationException("invalid_grant");
        _tokenRefresh.Setup(t => t.AcquireAccessTokenAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ThrowsAsync(refused);

        await CreateProbe().ProbeAsync(Target(TrueLayerFailureClassifier.ProviderName), ConnectionHealthProbeReason.Suspect);

        _health.Verify(h => h.RecordConnectionProbeFailureAsync(
            It.Is<BankConnectionRef>(c => c.Provider == TrueLayerFailureClassifier.ProviderName), refused, It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
