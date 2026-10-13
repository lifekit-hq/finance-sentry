namespace FinanceSentry.Tests.Integration.Auth;

using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Xunit;

/// <summary>
/// GET and PATCH /me/preferences carry the account tier of the app-shell standard: a present top-level field
/// replaces the stored one wholesale, an omitted one is untouched, and the response is always the full object.
/// </summary>
public class MePreferencesTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>
{
    private const string Password = "TestPass123!";
    private const string Url = "/api/v1/me/preferences";

    [Fact]
    public async Task Get_ForANewPerson_DefaultsToSystemWithNoSeed()
    {
        using var client = await ClientAsync();

        var json = await client.GetStringAsync(Url);

        using var doc = JsonDocument.Parse(json);
        var appearance = doc.RootElement.GetProperty("appearance");
        appearance.GetProperty("theme").GetString().Should().Be("system");
        appearance.TryGetProperty("seed", out _).Should().BeFalse();
        appearance.TryGetProperty("intensity", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Patch_WritesTheWholeAppearance_AndReturnsTheFullObject()
    {
        using var client = await ClientAsync();

        var response = await PatchAsync(client, """{"appearance":{"theme":"dark","seed":"#4F46E5","intensity":0.3}}""");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var saved = await response.Content.ReadFromJsonAsync<PreferencesShape>();
        saved!.Appearance.Should().Be(new AppearanceShape("dark", "#4f46e5", 0.3));
        (await client.GetFromJsonAsync<PreferencesShape>(Url))!.Appearance.Should().Be(saved.Appearance);
    }

    [Fact]
    public async Task Patch_WithAnOmittedSeed_ClearsTheStoredSeed()
    {
        using var client = await ClientAsync();
        await PatchAsync(client, """{"appearance":{"theme":"light","seed":"#4f46e5","intensity":0.3}}""");

        var response = await PatchAsync(client, """{"appearance":{"theme":"light"}}""");

        (await response.Content.ReadFromJsonAsync<PreferencesShape>())!.Appearance
            .Should().Be(new AppearanceShape("light", null, null));
    }

    [Fact]
    public async Task Patch_WithoutAppearance_LeavesItUntouched()
    {
        using var client = await ClientAsync();
        await PatchAsync(client, """{"appearance":{"theme":"dark","seed":"#4f46e5"}}""");

        var response = await PatchAsync(client, "{}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<PreferencesShape>())!.Appearance
            .Should().Be(new AppearanceShape("dark", "#4f46e5", null));
    }

    [Theory]
    [InlineData("""{"appearance":{"theme":"sepia"}}""")]
    [InlineData("""{"appearance":{"theme":"dark","seed":"blue"}}""")]
    [InlineData("""{"appearance":{"theme":"dark","intensity":1.5}}""")]
    public async Task Patch_WithAnInvalidValue_Returns400AndSavesNothing(string body)
    {
        using var client = await ClientAsync();

        var response = await PatchAsync(client, body);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("VALIDATION_ERROR");
        (await client.GetFromJsonAsync<PreferencesShape>(Url))!.Appearance.Theme.Should().Be("system");
    }

    [Fact]
    public async Task Preferences_AreThePersonsOwn()
    {
        using var first = await ClientAsync();
        using var second = await ClientAsync();
        await PatchAsync(first, """{"appearance":{"theme":"dark","seed":"#4f46e5"}}""");

        (await second.GetFromJsonAsync<PreferencesShape>(Url))!.Appearance
            .Should().Be(new AppearanceShape("system", null, null));
    }

    [Fact]
    public async Task Get_WithoutASession_Returns401()
    {
        using var anonymous = factory.CreateClient();

        var response = await anonymous.GetAsync(Url);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private async Task<HttpClient> ClientAsync()
    {
        var email = $"prefs-{Guid.NewGuid():N}@test.com";
        await factory.EnsureUserExistsAsync(email, Password);
        return factory.CookieClient(("fs_access_token", await factory.SignInAsync(email, Password)));
    }

    private static Task<HttpResponseMessage> PatchAsync(HttpClient client, string json) =>
        client.PatchAsync(Url, new StringContent(json, Encoding.UTF8, "application/json"));

    private sealed record AppearanceShape(string Theme, string? Seed, double? Intensity);

    private sealed record PreferencesShape(AppearanceShape Appearance);
}
