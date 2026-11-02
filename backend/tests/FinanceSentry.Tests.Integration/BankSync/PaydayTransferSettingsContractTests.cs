namespace FinanceSentry.Tests.Integration.BankSync;

using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

/// <summary>
/// PUT /payday-transfer/settings through the real MVC pipeline. The request's <c>Range</c> limits must be bound to the
/// record's constructor parameters: on the property they make MVC validation throw (a 500 for every call, valid or not).
/// The oracle is the public contract: 200 echoing the saved values inside the limits, 400 outside them.
/// </summary>
public class PaydayTransferSettingsContractTests(BankSyncApiFactory factory) : IClassFixture<BankSyncApiFactory>
{
    private const string SettingsUrl = "/api/v1/payday-transfer/settings";

    private readonly HttpClient _client = factory.CreateAuthenticatedClient();

    private sealed record SettingsShape(decimal FixedOutflowUah, decimal RoundingStepEur);

    [Fact]
    public async Task Put_InsideLimits_Returns200_WithSavedValues()
    {
        var response = await _client.PutAsJsonAsync(SettingsUrl, new { fixedOutflowUah = 20000m, roundingStepEur = 100m });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<SettingsShape>();
        body.Should().Be(new SettingsShape(20000m, 100m));
    }

    [Theory]
    [InlineData(-1, 10)]
    [InlineData(0, 0)]
    [InlineData(1, 1_000_000_001)]
    [InlineData(1_000_000_001, 10)]
    public async Task Put_OutsideLimits_Returns400(double fixedOutflowUah, double roundingStepEur)
    {
        var response = await _client.PutAsJsonAsync(SettingsUrl, new { fixedOutflowUah, roundingStepEur });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
