namespace FinanceSentry.Tests.Unit.BankSync.Monobank;

using System.Net;
using FinanceSentry.Modules.BankSync.Infrastructure.Monobank;
using FluentAssertions;
using Xunit;

/// <summary>
/// Contract tests for Monobank GET /personal/client-info response mapping (T012).
/// Mocked HTTP handler — no live Monobank calls.
/// </summary>
public class MonobankClientInfoContractTests
{
    private const string Token = "uTestToken123";

    private const string ClientInfoBody = """
        {
          "clientId": "3MSaMMtczs",
          "name": "Мазепа Іван",
          "webHookUrl": "",
          "permissions": "psfj",
          "accounts": [
            {
              "id": "kKGVoZuHWzqVoZuH",
              "sendId": "uHWzqVoZuH",
              "balance": 10000000,
              "creditLimit": 500000,
              "type": "black",
              "currencyCode": 980,
              "cashbackType": "UAH",
              "maskedPan": ["537541******1234"],
              "iban": "UA733220010000026201234567890"
            },
            {
              "id": "yellowAcct000001",
              "sendId": "yellowSend",
              "balance": 250000,
              "creditLimit": 0,
              "type": "yellow",
              "currencyCode": 840,
              "cashbackType": "None",
              "maskedPan": [],
              "iban": "UA733220010000026201234567891"
            }
          ]
        }
        """;

    [Fact]
    public async Task GetClientInfo_HappyPath_MapsClientAndAccounts()
    {
        var handler = new MonobankStubHttpHandler().Enqueue(HttpStatusCode.OK, ClientInfoBody);

        var result = await handler.BuildClient().GetClientInfoAsync(Token);

        result.ClientId.Should().Be("3MSaMMtczs");
        result.Name.Should().Be("Мазепа Іван");
        result.Accounts.Should().HaveCount(2);

        var black = result.Accounts[0];
        black.Id.Should().Be("kKGVoZuHWzqVoZuH");
        black.Name.Should().Be("black UAH");
        // Carries a credit line, so the product-name map is overridden: it's a liability
        // account, not a checking asset (its balance includes the limit).
        black.Type.Should().Be("credit");
        black.MaskedPan.Should().Be("537541******1234");
        black.CurrencyCode.Should().Be(980);
        black.Balance.Should().Be(10000000L); // raw kopecks at the client layer
        black.CreditLimit.Should().Be(500000L);
    }

    [Fact]
    public async Task GetClientInfo_KeepsIbanAndEveryMaskedPan()
    {
        var body = ClientInfoBody.Replace("[\"537541******1234\"]", "[\"537541******1234\", \"516875******9876\"]");
        var handler = new MonobankStubHttpHandler().Enqueue(HttpStatusCode.OK, body);

        var result = await handler.BuildClient().GetClientInfoAsync(Token);

        result.Accounts[0].Iban.Should().Be("UA733220010000026201234567890");
        result.Accounts[0].MaskedPans.Should().Equal("537541******1234", "516875******9876");
        result.Accounts[0].MaskedPan.Should().Be("516875******9876", "the single-PAN field still carries the last PAN");
        result.Accounts[1].MaskedPans.Should().BeEmpty();
    }

    [Fact]
    public async Task GetClientInfo_CardWithoutCreditLimit_KeepsProductTypeMapping()
    {
        var body = ClientInfoBody.Replace("\"creditLimit\": 500000", "\"creditLimit\": 0");
        var handler = new MonobankStubHttpHandler().Enqueue(HttpStatusCode.OK, body);

        var result = await handler.BuildClient().GetClientInfoAsync(Token);

        result.Accounts[0].Type.Should().Be("checking");
    }

    [Fact]
    public async Task GetClientInfo_MapsCardTypes_YellowIsCredit()
    {
        var handler = new MonobankStubHttpHandler().Enqueue(HttpStatusCode.OK, ClientInfoBody);

        var result = await handler.BuildClient().GetClientInfoAsync(Token);

        var yellow = result.Accounts[1];
        yellow.Type.Should().Be("credit");
        yellow.Name.Should().Be("yellow USD");
        yellow.MaskedPan.Should().Be("0000"); // empty maskedPan array falls back to "0000"
    }

    [Fact]
    public async Task GetClientInfo_SendsTokenHeaderToClientInfoPath()
    {
        var handler = new MonobankStubHttpHandler().Enqueue(HttpStatusCode.OK, ClientInfoBody);

        await handler.BuildClient().GetClientInfoAsync(Token);

        handler.RequestPaths.Should().ContainSingle().Which.Should().Be("/personal/client-info");
        handler.RequestTokens.Should().ContainSingle().Which.Should().Be(Token);
    }

    [Fact]
    public async Task GetClientInfo_Unauthorized_ThrowsTokenInvalidMappedTo400()
    {
        var handler = new MonobankStubHttpHandler()
            .Enqueue(HttpStatusCode.Unauthorized, """{"errorDescription":"Unknown 'X-Token'"}""");

        var act = () => handler.BuildClient().GetClientInfoAsync("bad-token");

        (await act.Should().ThrowAsync<MonobankException>())
            .Which.Should().Match<MonobankException>(e =>
                e.ErrorCode == "MONOBANK_TOKEN_INVALID" && e.StatusCode == 400);
    }

    [Fact]
    public async Task GetClientInfo_Forbidden_ThrowsTokenInvalidMappedTo400()
    {
        // The real Monobank API answers 403 (not 401) for a bad/unknown X-Token.
        var handler = new MonobankStubHttpHandler()
            .Enqueue(HttpStatusCode.Forbidden, """{"errorDescription":"Unknown 'X-Token'"}""");

        var act = () => handler.BuildClient().GetClientInfoAsync("bad-token");

        (await act.Should().ThrowAsync<MonobankException>())
            .Which.Should().Match<MonobankException>(e =>
                e.ErrorCode == "MONOBANK_TOKEN_INVALID" && e.StatusCode == 400);
    }
}
