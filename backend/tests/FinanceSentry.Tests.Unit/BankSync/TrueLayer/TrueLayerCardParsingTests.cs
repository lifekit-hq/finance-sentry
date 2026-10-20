namespace FinanceSentry.Tests.Unit.BankSync.TrueLayer;

using System.Net;
using FinanceSentry.Modules.BankSync.Infrastructure.TrueLayer;
using FinanceSentry.Tests.Unit.BankSync.Monobank;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using Microsoft.Extensions.Options;

public class TrueLayerCardParsingTests
{
    private static TrueLayerHttpClient Build(string body)
    {
        var handler = new MonobankStubHttpHandler().Enqueue(HttpStatusCode.OK, body);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(TrueLayerHttpClient.ApiClientName))
            .Returns(new HttpClient(handler) { BaseAddress = new Uri("https://api.truelayer-sandbox.com") });
        return new TrueLayerHttpClient(
            factory.Object, Options.Create(new TrueLayerOptions()), NullLogger<TrueLayerHttpClient>.Instance);
    }

    [Fact]
    public async Task ListCards_KeepsValidTo_AndLeavesItNullWhenAbsent()
    {
        var client = Build("""
            {"results":[
              {"account_id":"c1","partial_card_number":"1234","valid_to":"2027-01"},
              {"account_id":"c2","partial_card_number":"5678"}
            ]}
            """);

        var cards = await client.ListCardsAsync("token");

        cards[0].ValidTo.Should().Be("2027-01");
        cards[1].ValidTo.Should().BeNull();
    }

    [Fact]
    public async Task ListAccounts_KeepsIban()
    {
        var client = Build("""
            {"results":[{"account_id":"a1","account_type":"TRANSACTION","account_number":{"iban":"IE29AIBK93115212345678"}}]}
            """);

        var accounts = await client.ListAccountsAsync("token");

        accounts.Should().ContainSingle().Which.Iban.Should().Be("IE29AIBK93115212345678");
    }
}
