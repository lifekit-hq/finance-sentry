namespace FinanceSentry.Tests.Unit.Configuration;

using FinanceSentry.Modules.BankSync;
using FinanceSentry.Modules.BankSync.Infrastructure.TrueLayer;
using FinanceSentry.Modules.CryptoSync;
using FinanceSentry.Modules.CryptoSync.Infrastructure.RevolutX;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

/// <summary>
/// Registers the real BankSync / CryptoSync modules, so the wiring the options tests bypass
/// (<c>BindConfiguration</c>, the root <c>PublicApiBaseUrl</c> copy, <c>ValidateOnStart</c>) is exercised.
/// </summary>
public class ProviderModuleRegistrationTests
{
    private static ServiceProvider BuildBankSync(Dictionary<string, string?> settings)
    {
        var config = Config(settings);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(config);
        services.AddBankSyncModule(config);
        return services.BuildServiceProvider();
    }

    private static ServiceProvider BuildCryptoSync(Dictionary<string, string?> settings)
    {
        var config = Config(settings);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(config);
        services.AddCryptoSyncModule(config);
        return services.BuildServiceProvider();
    }

    private static IConfiguration Config(Dictionary<string, string?> settings)
    {
        var all = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = "Host=localhost;Database=unused",
            ["Deduplication:MasterKeyBase64"] = Convert.ToBase64String(new byte[32]),
        };
        foreach (var (key, value) in settings)
            all[key] = value;
        return new ConfigurationBuilder().AddInMemoryCollection(all).Build();
    }

    [Fact]
    public void BankSync_RootPublicApiBaseUrl_BuildsTheTrueLayerRedirectUri()
    {
        using var sp = BuildBankSync(new() { ["PublicApiBaseUrl"] = "https://api.example/" });

        sp.GetRequiredService<IOptions<TrueLayerOptions>>().Value.RedirectUri
            .Should().Be("https://api.example/api/v1/accounts/truelayer/callback");
    }

    [Fact]
    public void BankSync_TrueLayerSection_IsBoundFromConfiguration()
    {
        using var sp = BuildBankSync(new()
        {
            ["TrueLayer:ClientId"] = "id",
            ["TrueLayer:ClientSecret"] = "secret",
            ["TrueLayer:CallbackPath"] = "/cb",
        });

        var options = sp.GetRequiredService<IOptions<TrueLayerOptions>>().Value;

        options.IsConfigured.Should().BeTrue();
        options.RedirectUri.Should().Be("http://localhost:5001/cb");
    }

    [Theory]
    [InlineData("PublicApiBaseUrl", "not a url")]
    [InlineData("TrueLayer:AuthBaseUrl", "not a url")]
    [InlineData("TrueLayer:ClientId", "id")]
    [InlineData("Monobank:BaseUrl", "nope")]
    public void BankSync_MalformedConfiguration_FailsHostStartValidation(string key, string value)
    {
        using var sp = BuildBankSync(new() { [key] = value });

        var act = () => sp.GetRequiredService<IStartupValidator>().Validate();

        act.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public void BankSync_EmptyConfiguration_PassesHostStartValidation()
    {
        using var sp = BuildBankSync([]);

        var act = () => sp.GetRequiredService<IStartupValidator>().Validate();

        act.Should().NotThrow();
    }

    [Fact]
    public void CryptoSync_RevolutXSection_IsBoundFromConfiguration()
    {
        using var sp = BuildCryptoSync(new() { ["RevolutX:BaseUrl"] = "https://revx.example" });

        sp.GetRequiredService<IOptions<RevolutXOptions>>().Value.BaseUrl.Should().Be("https://revx.example");
    }

    [Theory]
    [InlineData("Binance:BaseUrl", "binance")]
    [InlineData("Binance:RecvWindowMs", "0")]
    [InlineData("RevolutX:BaseUrl", "")]
    public void CryptoSync_MalformedConfiguration_FailsHostStartValidation(string key, string value)
    {
        using var sp = BuildCryptoSync(new() { [key] = value });

        var act = () => sp.GetRequiredService<IStartupValidator>().Validate();

        act.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public void CryptoSync_EmptyConfiguration_PassesHostStartValidation()
    {
        using var sp = BuildCryptoSync([]);

        var act = () => sp.GetRequiredService<IStartupValidator>().Validate();

        act.Should().NotThrow();
    }
}
