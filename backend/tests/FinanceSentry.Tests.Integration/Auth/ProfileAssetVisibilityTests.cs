namespace FinanceSentry.Tests.Integration.Auth;

using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

/// <summary>
/// GET/PUT /profile carries the three asset-visibility settings: new accounts start on today's best UX
/// (hide empty on, hide small off, threshold 1), a client that omits them leaves them untouched, and the
/// threshold must be above zero and at most 100 in the base currency.
/// </summary>
public class ProfileAssetVisibilityTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>
{
    private const string Password = "TestPass123!";

    [Fact]
    public async Task Get_ForANewUser_ReturnsTheDefaults()
    {
        using var client = await ClientAsync();

        var profile = await client.GetFromJsonAsync<ProfileShape>("/api/v1/profile");

        profile.Should().Be(new ProfileShape(true, false, 1m));
    }

    [Fact]
    public async Task Put_StoresTheSettings_AndAnOmittingClientLeavesThemAlone()
    {
        using var client = await ClientAsync();

        var saved = await client.PutAsJsonAsync("/api/v1/profile", Body(new { hideEmptyBalances = false, hideSmallBalances = true, smallBalanceThreshold = 5m }));
        saved.StatusCode.Should().Be(HttpStatusCode.OK);
        (await saved.Content.ReadFromJsonAsync<ProfileShape>()).Should().Be(new ProfileShape(false, true, 5m));

        var legacy = await client.PutAsJsonAsync("/api/v1/profile", Body(new { }));
        legacy.StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetFromJsonAsync<ProfileShape>("/api/v1/profile")).Should().Be(new ProfileShape(false, true, 5m));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(100.01)]
    public async Task Put_WithAnOutOfRangeThreshold_Returns400AndSavesNothing(double threshold)
    {
        using var client = await ClientAsync();

        var response = await client.PutAsJsonAsync("/api/v1/profile", Body(new { smallBalanceThreshold = (decimal)threshold }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("VALIDATION_ERROR");
        (await client.GetFromJsonAsync<ProfileShape>("/api/v1/profile")).Should().Be(new ProfileShape(true, false, 1m));
    }

    [Fact]
    public async Task Put_WithTheThresholdOnTheCap_Returns200()
    {
        using var client = await ClientAsync();

        var response = await client.PutAsJsonAsync("/api/v1/profile", Body(new { smallBalanceThreshold = 100m }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<HttpClient> ClientAsync()
    {
        var email = $"asset-visibility-{Guid.NewGuid():N}@test.com";
        await factory.EnsureUserExistsAsync(email, Password);
        return factory.CookieClient(("fs_access_token", await factory.SignInAsync(email, Password)));
    }

    private static Dictionary<string, object?> Body(object visibility)
    {
        var body = new Dictionary<string, object?>
        {
            ["firstName"] = "Asset",
            ["lastName"] = "Visibility",
            ["baseCurrency"] = "USD",
            ["emailAlerts"] = true,
            ["lowBalanceAlerts"] = false,
            ["lowBalanceThreshold"] = 100m,
            ["syncFailureAlerts"] = true,
            ["safeWithdrawalRate"] = 0.04m,
            ["realAnnualReturn"] = 0.05m,
        };
        foreach (var property in visibility.GetType().GetProperties())
            body[property.Name] = property.GetValue(visibility);
        return body;
    }

    private sealed record ProfileShape(bool HideEmptyBalances, bool HideSmallBalances, decimal SmallBalanceThreshold);
}
