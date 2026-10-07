using System.Net;
using FinanceSentry.Modules.BrokerageSync.Application.Services;
using FinanceSentry.Modules.BrokerageSync.Domain;
using FinanceSentry.Modules.BrokerageSync.Domain.Repositories;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.Inzhur;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace FinanceSentry.Tests.Unit.BrokerageSync.Inzhur;

/// <summary>The daily read against a fake cabinet: refresh, two GETs, holdings stored; never a sign-in.</summary>
public class InzhurSyncServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 7, 0, 0, TimeSpan.Zero);

    private readonly Guid _userId = Guid.NewGuid();
    private readonly FakeEncryption _encryption = new();
    private readonly FakeInzhurHandler _cabinet = new();
    private readonly Mock<IInzhurCredentialRepository> _credentials = new(MockBehavior.Loose);
    private readonly Mock<IBrokerageHoldingRepository> _holdings = new(MockBehavior.Loose);
    private readonly List<BrokerageHolding> _upserted = [];
    private readonly List<BrokerageHolding> _removed = [];
    private List<BrokerageHolding> _persisted = [];

    public InzhurSyncServiceTests()
    {
        _holdings.Setup(h => h.UpsertRangeAsync(It.IsAny<IEnumerable<BrokerageHolding>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<BrokerageHolding>, CancellationToken>((rows, _) => _upserted.AddRange(rows))
            .Returns(Task.CompletedTask);
        _holdings.Setup(h => h.GetByUserIdUnscopedAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _persisted.Concat(_upserted).ToList());
        _holdings.Setup(h => h.RemoveRange(It.IsAny<IEnumerable<BrokerageHolding>>()))
            .Callback<IEnumerable<BrokerageHolding>>(rows => _removed.AddRange(rows));
    }

    private InzhurCredential ActiveCredential()
    {
        var credential = new InzhurCredential(_userId, Secret(InzhurFakes.Phone), Secret(InzhurFakes.Password));
        credential.StartSession(Secret(InzhurFakes.Session().Serialize()), Now.UtcDateTime.AddDays(-3));
        _credentials.Setup(r => r.GetByUserIdUnscopedAsync(_userId, It.IsAny<CancellationToken>())).ReturnsAsync(credential);
        return credential;
    }

    private EncryptedSecret Secret(string plaintext)
    {
        var r = _encryption.Encrypt(plaintext);
        return new EncryptedSecret(r.Ciphertext, r.Iv, r.AuthTag, r.KeyVersion);
    }

    private InzhurSyncService Service() => new(
        _credentials.Object,
        _holdings.Object,
        new InzhurApiClient(new HttpClient(_cabinet), Options.Create(new InzhurOptions()), NullLogger<InzhurApiClient>.Instance),
        _encryption,
        new ManualClock(Now),
        NullLogger<InzhurSyncService>.Instance);

    private void CabinetAnswersWith(object assets, object account, string rotatedRefresh = "fake-refresh-2")
        => _cabinet
            .Then(_ =>
            {
                var response = InzhurFakes.Json(HttpStatusCode.OK, new { accessToken = "fresh-token" });
                response.Headers.Add("Set-Cookie", $"{InzhurFakes.RefreshCookieName}={rotatedRefresh}; Domain={InzhurFakes.AuthHost}; Path=/; Secure; HttpOnly");
                return response;
            })
            .Then(InzhurFakes.Json(HttpStatusCode.OK, assets))
            .Then(InzhurFakes.Json(HttpStatusCode.OK, account));

    [Fact]
    public async Task Sync_refreshes_reads_and_stores_holdings_and_the_rotated_session()
    {
        var credential = ActiveCredential();
        CabinetAnswersWith(InzhurFakes.UserAssets(InzhurFakes.Fund("Fund A", 10m, 1_200m, 1_000m)), InzhurFakes.BrokerAccount(500m, 0m));

        var outcome = await Service().SyncAsync(_userId);

        outcome.Should().Be(InzhurSyncOutcome.Synced);
        _upserted.Select(h => (h.Symbol, h.InstrumentType, h.Provider)).Should().BeEquivalentTo(
            [("Fund A", "REIT", "inzhur"), ("UAH Cash", "CASH", "inzhur")]);
        credential.LastSyncAt.Should().Be(Now.UtcDateTime);
        credential.SessionRefreshedAt.Should().Be(Now.UtcDateTime);

        var stored = InzhurSession.Deserialize(_encryption.Decrypt(credential.EncryptedSession, [], [], 1));
        stored.AccessToken.Should().Be("fresh-token");
        stored.Cookies.Should().ContainSingle(c => c.Name == InzhurFakes.RefreshCookieName).Which.Value.Should().Be("fake-refresh-2");
    }

    [Fact]
    public async Task Sync_only_refreshes_and_reads_never_signs_in()
    {
        ActiveCredential();
        CabinetAnswersWith(InzhurFakes.UserAssets(), InzhurFakes.BrokerAccount(1m, 0m));

        await Service().SyncAsync(_userId);

        _cabinet.Requests.Select(r => $"{r.Method} {r.Uri.AbsolutePath}").Should().Equal(
            "POST /auth/api/v1/auth/refresh",
            "GET /core/api/v1/user-assets",
            "GET /core/api/v1/users/broker-account");
    }

    [Fact]
    public async Task A_position_Inzhur_no_longer_returns_leaves_and_other_providers_rows_stay()
    {
        ActiveCredential();
        var sold = new BrokerageHolding(_userId, "Sold Fund", "REIT", 1m, 1m, "inzhur");
        var ibkr = new BrokerageHolding(_userId, "AAPL", "STK", 1m, 1m, "ibkr");
        _persisted = [sold, ibkr];
        CabinetAnswersWith(InzhurFakes.UserAssets(InzhurFakes.Fund("Fund A", 1m, 100m, 90m)), InzhurFakes.BrokerAccount(0m, 0m));

        await Service().SyncAsync(_userId);

        _removed.Should().ContainSingle().Which.Should().BeSameAs(sold);
    }

    [Fact]
    public async Task A_dead_session_flips_the_connection_to_reauth_required_and_drops_it()
    {
        var credential = ActiveCredential();
        _cabinet.Then(InzhurFakes.Error(HttpStatusCode.Unauthorized, "Invalid or expired refresh token"));

        var act = () => Service().SyncAsync(_userId);

        (await act.Should().ThrowAsync<InzhurApiException>()).Which.Kind.Should().Be(InzhurFailureKind.ReauthRequired);
        credential.Status.Should().Be(InzhurConnectionStatus.ReauthRequired);
        credential.HasSession.Should().BeFalse();
        credential.HasLoginSecrets.Should().BeTrue("a reconnect then asks only for the SMS code");
        _cabinet.Requests.Should().ContainSingle("nothing is read and no sign-in is tried once the session is gone");
    }

    [Fact]
    public async Task Inzhur_being_down_keeps_the_connection_and_records_the_error()
    {
        var credential = ActiveCredential();
        _cabinet.Then(InzhurFakes.Error(HttpStatusCode.ServiceUnavailable, "maintenance"));

        var act = () => Service().SyncAsync(_userId);

        (await act.Should().ThrowAsync<InzhurApiException>()).Which.IsTransient.Should().BeTrue();
        credential.Status.Should().Be(InzhurConnectionStatus.Active);
        credential.HasSession.Should().BeTrue();
        credential.LastSyncError.Should().Contain("503");
    }

    [Fact]
    public async Task A_connection_waiting_for_the_owner_is_skipped_without_a_call()
    {
        var credential = ActiveCredential();
        credential.MarkReauthRequired("expired");

        (await Service().SyncAsync(_userId)).Should().Be(InzhurSyncOutcome.Skipped);
        _cabinet.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task A_cabinet_answer_without_assets_keeps_the_stored_holdings_and_records_an_error()
    {
        var credential = ActiveCredential();
        var held = new BrokerageHolding(_userId, "Fund A", "REIT", 1m, 1m, "inzhur");
        _persisted = [held];
        _cabinet
            .Then(InzhurFakes.Json(HttpStatusCode.OK, new { accessToken = "fresh-token" }))
            .Then(InzhurFakes.Json(HttpStatusCode.OK, new { }))
            .Then(InzhurFakes.Json(HttpStatusCode.OK, InzhurFakes.BrokerAccount(0m, 0m)));

        var act = () => Service().SyncAsync(_userId);

        (await act.Should().ThrowAsync<InzhurApiException>()).Which.Kind.Should().Be(InzhurFailureKind.Unexpected);
        _upserted.Should().BeEmpty();
        _removed.Should().BeEmpty();
        credential.LastSyncAt.Should().BeNull();
        credential.Status.Should().Be(InzhurConnectionStatus.Active);
        credential.LastSyncError.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Assets_with_amounts_but_no_readable_quantity_are_refused_and_stored_rows_stay()
    {
        var credential = ActiveCredential();
        _persisted = [new BrokerageHolding(_userId, "Fund A", "REIT", 1m, 1m, "inzhur")];
        var quantityMoved = new
        {
            id = 1,
            type = "fund",
            name = "Fund A",
            prices = new { sellUAH = 100m },
            details = new { totalAmountUAH = 1_200m, investedUAH = 1_000m },
        };
        CabinetAnswersWith(InzhurFakes.UserAssets(quantityMoved), InzhurFakes.BrokerAccount(500m, 0m));

        var act = () => Service().SyncAsync(_userId);

        (await act.Should().ThrowAsync<InzhurApiException>()).Which.Kind.Should().Be(InzhurFailureKind.Unexpected);
        _upserted.Should().BeEmpty();
        _removed.Should().BeEmpty();
        credential.LastSyncAt.Should().BeNull();
        credential.LastSyncError.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task A_sold_out_account_with_only_zero_amount_assets_clears_to_empty()
    {
        var credential = ActiveCredential();
        var sold = new BrokerageHolding(_userId, "Fund A", "REIT", 1m, 1m, "inzhur");
        _persisted = [sold];
        var soldOut = new
        {
            id = 1,
            type = "fund",
            name = "Fund A",
            details = new { certificatesOwnedQuantity = 0m, totalAmountUAH = 0m, investedUAH = 0m },
        };
        CabinetAnswersWith(InzhurFakes.UserAssets(soldOut), InzhurFakes.BrokerAccount(0m, 0m));

        (await Service().SyncAsync(_userId)).Should().Be(InzhurSyncOutcome.Synced);

        _upserted.Should().BeEmpty();
        _removed.Should().ContainSingle().Which.Should().BeSameAs(sold);
        credential.LastSyncAt.Should().Be(Now.UtcDateTime);
    }
}
