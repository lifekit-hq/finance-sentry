using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.BrokerageSync.Application.Connect;
using FinanceSentry.Modules.BrokerageSync.Application.Services;
using FinanceSentry.Modules.BrokerageSync.Domain;
using FinanceSentry.Modules.BrokerageSync.Domain.Exceptions;
using FinanceSentry.Modules.BrokerageSync.Domain.Repositories;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.Inzhur;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace FinanceSentry.Tests.Unit.BrokerageSync.Inzhur;

/// <summary>The owner hands over the session he signed in to on inzhur.reit; finance-sentry proves it with one refresh.</summary>
public class InzhurConnectorTests
{
    private const string Pasted = "fake-pasted-refresh";

    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    private readonly Guid _userId = Guid.NewGuid();
    private readonly FakeEncryption _encryption = new();
    private readonly ManualClock _clock = new(Now);
    private readonly Mock<IInzhurCredentialRepository> _credentials = new(MockBehavior.Loose);
    private readonly Mock<IBrokerageHoldingRepository> _holdings = new(MockBehavior.Loose);
    private readonly Mock<IInzhurApiClient> _api = new(MockBehavior.Strict);
    private readonly Mock<IInzhurSyncService> _sync = new(MockBehavior.Loose);
    private readonly Mock<IAlertGeneratorService> _alerts = new(MockBehavior.Loose);
    private readonly CapturingLogger _logger = new();
    private readonly List<InzhurSession> _refreshed = [];
    private InzhurCredential? _stored;

    public InzhurConnectorTests()
    {
        _credentials.Setup(r => r.GetByUserIdAsync(_userId, It.IsAny<CancellationToken>())).ReturnsAsync(() => _stored);
        _credentials.Setup(r => r.AddAsync(It.IsAny<InzhurCredential>(), It.IsAny<CancellationToken>()))
            .Callback<InzhurCredential, CancellationToken>((c, _) => _stored = c)
            .Returns(Task.CompletedTask);
    }

    private InzhurConnector Connector() => new(
        _credentials.Object, _holdings.Object, _api.Object, _sync.Object, _encryption, _alerts.Object,
        Options.Create(new InzhurOptions()), _clock, _logger);

    private void RefreshReturns(InzhurSession session)
        => _api.Setup(a => a.RefreshAsync(It.IsAny<InzhurSession>(), It.IsAny<CancellationToken>()))
            .Callback<InzhurSession, CancellationToken>((s, _) => _refreshed.Add(s))
            .ReturnsAsync(session);

    private void RefreshThrows(InzhurFailureKind kind)
        => _api.Setup(a => a.RefreshAsync(It.IsAny<InzhurSession>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InzhurApiException(kind, "Inzhur refresh returned 401."));

    private string Plain(byte[] ciphertext) => _encryption.Decrypt(ciphertext, [], [], 1);

    [Fact]
    public async Task A_pasted_session_that_refreshes_is_stored_encrypted_activated_and_read_once()
    {
        RefreshReturns(InzhurFakes.Session(refreshValue: "fake-rotated"));

        var result = await Connector().ConnectSessionAsync(_userId, Pasted, default);

        result.Should().Be(new InzhurConnectResult(InzhurConnectResult.Connected));
        var sent = _refreshed.Should().ContainSingle().Subject;
        sent.Cookies.Should().ContainSingle().Which.Should().Match<InzhurCookie>(c =>
            c.Name == "refreshToken" && c.Value == Pasted && c.Domain == "api.inzhur.reit" && c.Secure && c.HttpOnly);
        _stored!.Status.Should().Be(InzhurConnectionStatus.Active);
        _stored.SessionStartedAt.Should().Be(Now.UtcDateTime);
        var stored = InzhurSession.Deserialize(Plain(_stored.EncryptedSession));
        stored.AccessToken.Should().Be(InzhurFakes.AccessToken);
        stored.Cookies.Should().ContainSingle().Which.Value.Should().Be("fake-rotated", "the jar as the refresh left it is stored");
        _credentials.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _sync.Verify(s => s.SyncAsync(_userId, It.IsAny<CancellationToken>()), Times.Once);
        _alerts.Verify(a => a.ResolveSyncFailureAlertAsync(_userId, "inzhur", null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("  fake-pasted-refresh\n")]
    [InlineData("refreshToken=fake-pasted-refresh")]
    [InlineData("\"fake-pasted-refresh\"")]
    public async Task The_cookie_name_quotes_and_surrounding_space_are_stripped(string pasted)
    {
        RefreshReturns(InzhurFakes.Session());

        await Connector().ConnectSessionAsync(_userId, pasted, default);

        _refreshed.Single().Cookies.Single().Value.Should().Be(Pasted);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("refreshToken=")]
    public async Task Nothing_pasted_is_refused_before_Inzhur_is_called(string? pasted)
    {
        var act = () => Connector().ConnectSessionAsync(_userId, pasted, default);

        var error = (await act.Should().ThrowAsync<InzhurConnectException>()).Which;
        error.ErrorCode.Should().Be(InzhurErrorCodes.SessionRequired);
        error.StatusCode.Should().Be(400);
        _api.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("refreshToken=fake; Path=/")]
    [InlineData("fake value")]
    [InlineData("fake,value")]
    public async Task A_paste_that_is_not_a_bare_cookie_value_is_refused_before_Inzhur_is_called(string pasted)
    {
        var act = () => Connector().ConnectSessionAsync(_userId, pasted, default);

        (await act.Should().ThrowAsync<InzhurConnectException>()).Which.ErrorCode.Should().Be(InzhurErrorCodes.SessionInvalid);
        _api.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task An_oversized_paste_is_refused()
    {
        var act = () => Connector().ConnectSessionAsync(_userId, new string('x', InzhurConnector.MaxRefreshCookieLength + 1), default);

        (await act.Should().ThrowAsync<InzhurConnectException>()).Which.ErrorCode.Should().Be(InzhurErrorCodes.SessionInvalid);
    }

    [Theory]
    [InlineData(InzhurFailureKind.ReauthRequired, InzhurErrorCodes.SessionRejected, 422)]
    [InlineData(InzhurFailureKind.RateLimited, InzhurErrorCodes.Unavailable, 503)]
    [InlineData(InzhurFailureKind.Unavailable, InzhurErrorCodes.Unavailable, 503)]
    [InlineData(InzhurFailureKind.Unexpected, InzhurErrorCodes.ConnectFailed, 502)]
    public async Task A_session_Inzhur_refuses_is_not_stored(InzhurFailureKind kind, string errorCode, int status)
    {
        RefreshThrows(kind);

        var act = () => Connector().ConnectSessionAsync(_userId, Pasted, default);

        var error = (await act.Should().ThrowAsync<InzhurConnectException>()).Which;
        error.ErrorCode.Should().Be(errorCode);
        error.StatusCode.Should().Be(status);
        _stored.Should().BeNull();
        _credentials.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _sync.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_refused_session_leaves_an_existing_connection_untouched()
    {
        RefreshReturns(InzhurFakes.Session(refreshValue: "fake-first"));
        await Connector().ConnectSessionAsync(_userId, Pasted, default);
        var before = _stored!.EncryptedSession;
        RefreshThrows(InzhurFailureKind.ReauthRequired);

        var act = () => Connector().ConnectSessionAsync(_userId, "fake-stale", default);

        await act.Should().ThrowAsync<InzhurConnectException>();
        _stored.EncryptedSession.Should().Equal(before);
        _stored.Status.Should().Be(InzhurConnectionStatus.Active);
    }

    [Fact]
    public async Task A_new_session_reactivates_a_connection_that_needed_the_owner()
    {
        _stored = new InzhurCredential(_userId);
        _stored.MarkReauthRequired("Inzhur refresh returned 401.");
        RefreshReturns(InzhurFakes.Session());

        await Connector().ConnectSessionAsync(_userId, Pasted, default);

        _credentials.Verify(r => r.AddAsync(It.IsAny<InzhurCredential>(), It.IsAny<CancellationToken>()), Times.Never);
        _stored.Status.Should().Be(InzhurConnectionStatus.Active);
        _stored.LastSyncError.Should().BeNull();
        _stored.HasSession.Should().BeTrue();
    }

    [Fact]
    public async Task The_pasted_value_never_reaches_a_log_line()
    {
        RefreshThrows(InzhurFailureKind.ReauthRequired);
        await FluentActions.Awaiting(() => Connector().ConnectSessionAsync(_userId, Pasted, default)).Should().ThrowAsync<InzhurConnectException>();
        RefreshReturns(InzhurFakes.Session());
        await Connector().ConnectSessionAsync(_userId, Pasted, default);

        _logger.Lines.Should().NotBeEmpty().And.OnlyContain(line => !line.Contains(Pasted) && !line.Contains("fake-refresh-1"));
    }

    [Fact]
    public async Task Disconnect_removes_the_connection_and_only_Inzhur_holdings()
    {
        RefreshReturns(InzhurFakes.Session());
        await Connector().ConnectSessionAsync(_userId, Pasted, default);

        await Connector().DisconnectAsync(_userId, default);

        _credentials.Verify(r => r.Delete(_stored!), Times.Once);
        _holdings.Verify(h => h.DeleteByUserIdAndProviderAsync(_userId, "inzhur", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Status_never_carries_a_secret()
    {
        RefreshReturns(InzhurFakes.Session());
        await Connector().ConnectSessionAsync(_userId, Pasted, default);

        var status = await Connector().GetStatusAsync(_userId, default);

        status.Should().Be(new InzhurConnectionStatusResult(InzhurConnectionStatus.Active, null, Now.UtcDateTime));
        System.Text.Json.JsonSerializer.Serialize(status).Should().NotContain(Pasted).And.NotContain("fake-refresh-1");
    }

    private sealed class CapturingLogger : ILogger<InzhurConnector>
    {
        public List<string> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Lines.Add(formatter(state, exception) + (exception is null ? string.Empty : " " + exception));
    }
}
