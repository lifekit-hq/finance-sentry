namespace FinanceSentry.Tests.Integration.Auth;

using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

/// <summary>
/// PUT /profile enforces the FIRE assumption bounds the Settings screen applies (withdrawal rate 0.5-10%,
/// real return 0-15%), so an API client cannot save a rate that makes the FIRE target collapse to zero.
/// </summary>
public class ProfileFireBoundsTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>
{
    private const string Password = "TestPass123!";

    [Theory]
    [InlineData(0, 0.05)]
    [InlineData(-0.04, 0.05)]
    [InlineData(0.0049, 0.05)]
    [InlineData(0.1001, 0.05)]
    [InlineData(0.04, -0.001)]
    [InlineData(0.04, 0.1501)]
    public async Task Put_WithOutOfRangeAssumption_Returns400AndSavesNothing(double withdrawalRate, double realReturn)
    {
        var email = $"fire-bounds-{Guid.NewGuid():N}@test.com";
        using var client = await ClientAsync(email);

        var response = await client.PutAsJsonAsync("/api/v1/profile", Body((decimal)withdrawalRate, (decimal)realReturn));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("VALIDATION_ERROR");
        var profile = await client.GetFromJsonAsync<ProfileShape>("/api/v1/profile");
        profile!.SafeWithdrawalRate.Should().Be(0.04m);
        profile.RealAnnualReturn.Should().Be(0.05m);
    }

    [Theory]
    [InlineData(0.005, 0)]
    [InlineData(0.10, 0.15)]
    public async Task Put_WithAssumptionsOnTheBounds_Returns200(double withdrawalRate, double realReturn)
    {
        using var client = await ClientAsync($"fire-bounds-{Guid.NewGuid():N}@test.com");

        var response = await client.PutAsJsonAsync("/api/v1/profile", Body((decimal)withdrawalRate, (decimal)realReturn));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var profile = await response.Content.ReadFromJsonAsync<ProfileShape>();
        profile!.SafeWithdrawalRate.Should().Be((decimal)withdrawalRate);
        profile.RealAnnualReturn.Should().Be((decimal)realReturn);
    }

    private async Task<HttpClient> ClientAsync(string email)
    {
        await factory.EnsureUserExistsAsync(email, Password);
        return factory.CookieClient(("fs_access_token", await factory.SignInAsync(email, Password)));
    }

    private static object Body(decimal withdrawalRate, decimal realReturn) => new
    {
        firstName = "Fire",
        lastName = "Bounds",
        baseCurrency = "USD",
        theme = "dark",
        emailAlerts = true,
        lowBalanceAlerts = false,
        lowBalanceThreshold = 100m,
        syncFailureAlerts = true,
        safeWithdrawalRate = withdrawalRate,
        realAnnualReturn = realReturn,
    };

    private sealed record ProfileShape(decimal SafeWithdrawalRate, decimal RealAnnualReturn);
}
