using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.BrokerageSync.Application.Connect;
using FinanceSentry.Modules.BrokerageSync.Application.Services;
using FinanceSentry.Modules.BrokerageSync.Domain;
using FinanceSentry.Modules.BrokerageSync.Domain.Exceptions;
using FinanceSentry.Modules.BrokerageSync.Domain.Repositories;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.Inzhur;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FinanceSentry.Tests.Unit.BrokerageSync.Inzhur;

/// <summary>The owner-initiated sign-in against a fake browser: the only path that can make Inzhur send an SMS.</summary>
public class InzhurConnectorTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    private readonly Guid _userId = Guid.NewGuid();
    private readonly FakeEncryption _encryption = new();
    private readonly ManualClock _clock = new(Now);
    private readonly Mock<IInzhurCredentialRepository> _credentials = new(MockBehavior.Loose);
    private readonly Mock<IBrokerageHoldingRepository> _holdings = new(MockBehavior.Loose);
    private readonly Mock<IInzhurBrowserLogin> _browser = new(MockBehavior.Strict);
    private readonly Mock<IInzhurSyncService> _sync = new(MockBehavior.Loose);
    private readonly Mock<IAlertGeneratorService> _alerts = new(MockBehavior.Loose);
    private InzhurCredential? _stored;

    public InzhurConnectorTests()
    {
        _browser.SetupGet(b => b.IsAvailable).Returns(true);
        _credentials.Setup(r => r.GetByUserIdAsync(_userId, It.IsAny<CancellationToken>())).ReturnsAsync(() => _stored);
        _credentials.Setup(r => r.AddAsync(It.IsAny<InzhurCredential>(), It.IsAny<CancellationToken>()))
            .Callback<InzhurCredential, CancellationToken>((c, _) => _stored = c)
            .Returns(Task.CompletedTask);
    }

    private InzhurConnector Connector() => new(
        _credentials.Object, _holdings.Object, _browser.Object, _sync.Object, _encryption, _alerts.Object, _clock,
        NullLogger<InzhurConnector>.Instance);

    private void BrowserStartReturns(InzhurLoginOutcome outcome)
        => _browser.Setup(b => b.StartAsync(_userId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(outcome);

    private string Plain(byte[] ciphertext) => _encryption.Decrypt(ciphertext, [], [], 1);

    [Fact]
    public async Task Typed_credentials_are_stored_encrypted_and_the_sign_in_waits_for_the_SMS_code()
    {
        var expires = Now.UtcDateTime.AddMinutes(3);
        BrowserStartReturns(new InzhurLoginOutcome.CodeRequired(expires));

        var result = await Connector().StartAsync(_userId, "+00 000 000-00-00", InzhurFakes.Password, default);

        result.Should().Be(new InzhurConnectResult(InzhurConnectResult.CodeRequired, CodeExpiresAt: expires));
        _browser.Verify(b => b.StartAsync(_userId, InzhurFakes.Phone, InzhurFakes.Password, It.IsAny<CancellationToken>()), Times.Once);
        Plain(_stored!.EncryptedPhone).Should().Be(InzhurFakes.Phone, "the cabinet sends the phone as digits only");
        _stored.Status.Should().Be(InzhurConnectionStatus.ReauthRequired, "nothing is connected until the code is accepted");
    }

    [Fact]
    public async Task A_correct_code_stores_the_session_activates_the_connection_and_reads_once()
    {
        BrowserStartReturns(new InzhurLoginOutcome.CodeRequired(Now.UtcDateTime.AddMinutes(3)));
        _browser.Setup(b => b.VerifyAsync(_userId, "123456", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InzhurLoginOutcome.Authenticated(InzhurFakes.Session()));
        await Connector().StartAsync(_userId, InzhurFakes.Phone, InzhurFakes.Password, default);

        var result = await Connector().VerifyAsync(_userId, "123 456", default);

        result.Status.Should().Be(InzhurConnectResult.Connected);
        _stored!.Status.Should().Be(InzhurConnectionStatus.Active);
        _stored.SessionStartedAt.Should().Be(Now.UtcDateTime);
        InzhurSession.Deserialize(Plain(_stored.EncryptedSession)).AccessToken.Should().Be(InzhurFakes.AccessToken);
        _sync.Verify(s => s.SyncAsync(_userId, It.IsAny<CancellationToken>()), Times.Once);
        _alerts.Verify(a => a.ResolveSyncFailureAlertAsync(_userId, "inzhur", null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_wrong_code_reports_the_attempts_left_and_keeps_waiting()
    {
        BrowserStartReturns(new InzhurLoginOutcome.CodeRequired(Now.UtcDateTime.AddMinutes(3)));
        _browser.Setup(b => b.VerifyAsync(_userId, "000000", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InzhurLoginOutcome.InvalidCode(2));
        await Connector().StartAsync(_userId, InzhurFakes.Phone, InzhurFakes.Password, default);

        var result = await Connector().VerifyAsync(_userId, "000000", default);

        result.Should().Be(new InzhurConnectResult(InzhurConnectResult.InvalidCode, AttemptsLeft: 2));
        _stored!.Status.Should().Be(InzhurConnectionStatus.ReauthRequired);
    }

    [Fact]
    public async Task A_reconnect_reuses_the_saved_phone_and_password()
    {
        BrowserStartReturns(new InzhurLoginOutcome.CodeRequired(Now.UtcDateTime.AddMinutes(3)));
        await Connector().StartAsync(_userId, InzhurFakes.Phone, InzhurFakes.Password, default);

        await Connector().StartAsync(_userId, null, null, default);

        _browser.Verify(b => b.StartAsync(_userId, InzhurFakes.Phone, InzhurFakes.Password, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Without_saved_or_typed_credentials_nothing_is_sent_to_Inzhur()
    {
        var act = () => Connector().StartAsync(_userId, null, null, default);

        (await act.Should().ThrowAsync<InzhurConnectException>()).Which.ErrorCode.Should().Be(InzhurErrorCodes.CredentialsRequired);
        _browser.Verify(b => b.StartAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Sign_ins_are_capped_per_day_so_the_owner_gets_at_most_that_many_SMS()
    {
        BrowserStartReturns(new InzhurLoginOutcome.CodeRequired(Now.UtcDateTime.AddMinutes(3)));
        for (var i = 0; i < InzhurCredential.MaxLoginAttemptsPerDay; i++)
            await Connector().StartAsync(_userId, InzhurFakes.Phone, InzhurFakes.Password, default);

        var act = () => Connector().StartAsync(_userId, null, null, default);

        (await act.Should().ThrowAsync<InzhurConnectException>()).Which.ErrorCode.Should().Be(InzhurErrorCodes.LoginLimit);
        _browser.Verify(
            b => b.StartAsync(_userId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Exactly(InzhurCredential.MaxLoginAttemptsPerDay));

        _clock.Now = Now.AddDays(1);
        await Connector().StartAsync(_userId, null, null, default);
    }

    [Fact]
    public async Task Rejected_credentials_are_forgotten_so_the_next_connect_asks_for_them()
    {
        BrowserStartReturns(new InzhurLoginOutcome.Failed(InzhurErrorCodes.InvalidCredentials, "rejected"));

        var act = () => Connector().StartAsync(_userId, InzhurFakes.Phone, "wrong-placeholder", default);

        (await act.Should().ThrowAsync<InzhurConnectException>()).Which.StatusCode.Should().Be(422);
        _stored!.HasLoginSecrets.Should().BeFalse();
    }

    [Fact]
    public async Task Without_a_sign_in_browser_the_connect_is_unavailable()
    {
        _browser.SetupGet(b => b.IsAvailable).Returns(false);

        var act = () => Connector().StartAsync(_userId, InzhurFakes.Phone, InzhurFakes.Password, default);

        (await act.Should().ThrowAsync<InzhurConnectException>()).Which.StatusCode.Should().Be(503);
        _stored.Should().BeNull();
    }

    [Fact]
    public async Task Disconnect_removes_the_connection_and_only_Inzhur_holdings()
    {
        BrowserStartReturns(new InzhurLoginOutcome.CodeRequired(Now.UtcDateTime.AddMinutes(3)));
        await Connector().StartAsync(_userId, InzhurFakes.Phone, InzhurFakes.Password, default);

        await Connector().DisconnectAsync(_userId, default);

        _credentials.Verify(r => r.Delete(_stored!), Times.Once);
        _holdings.Verify(h => h.DeleteByUserIdAndProviderAsync(_userId, "inzhur", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Status_never_carries_a_secret()
    {
        BrowserStartReturns(new InzhurLoginOutcome.CodeRequired(Now.UtcDateTime.AddMinutes(3)));
        await Connector().StartAsync(_userId, InzhurFakes.Phone, InzhurFakes.Password, default);

        var status = await Connector().GetStatusAsync(_userId, default);

        status.HasSavedCredentials.Should().BeTrue();
        System.Text.Json.JsonSerializer.Serialize(status).Should().NotContain(InzhurFakes.Phone).And.NotContain(InzhurFakes.Password);
    }
}
