namespace FinanceSentry.Tests.Unit.Configuration;

using FinanceSentry.Infrastructure.Fx;
using FinanceSentry.Modules.BankSync.Infrastructure.Monobank;
using FinanceSentry.Modules.BankSync.Infrastructure.TrueLayer;
using FinanceSentry.Modules.CryptoSync.Infrastructure.Binance;
using FinanceSentry.Modules.CryptoSync.Infrastructure.RevolutX;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

public class ProviderOptionsValidationTests
{
    private static IServiceProvider Build(Dictionary<string, string?> settings)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddOptions<TrueLayerOptions>().Bind(config.GetSection("TrueLayer")).ValidateDataAnnotations();
        services.AddSingleton<IValidateOptions<TrueLayerOptions>, TrueLayerOptionsValidator>();
        services.AddOptions<BinanceOptions>().Bind(config.GetSection("Binance")).ValidateDataAnnotations();
        services.AddOptions<RevolutXOptions>().Bind(config.GetSection("RevolutX")).ValidateDataAnnotations();
        services.AddOptions<MonobankOptions>().Bind(config.GetSection("Monobank")).ValidateDataAnnotations();
        services.AddOptions<ExchangeRatesOptions>().Bind(config.GetSection("ExchangeRates")).ValidateDataAnnotations();
        return services.BuildServiceProvider();
    }

    [Fact]
    public void EmptyConfiguration_StartsWithTheProductionDefaults()
    {
        var sp = Build([]);

        sp.GetRequiredService<IOptions<TrueLayerOptions>>().Value.IsConfigured.Should().BeFalse();
        sp.GetRequiredService<IOptions<TrueLayerOptions>>().Value.RedirectUri
            .Should().Be("http://localhost:5001/api/v1/accounts/truelayer/callback");
        sp.GetRequiredService<IOptions<BinanceOptions>>().Value.RecvWindowMs.Should().Be(5000);
        sp.GetRequiredService<IOptions<BinanceOptions>>().Value.DustThresholdUsd.Should().Be(0.01m);
        sp.GetRequiredService<IOptions<RevolutXOptions>>().Value.BaseUrl.Should().Be("https://revx.revolut.com");
        sp.GetRequiredService<IOptions<MonobankOptions>>().Value.BaseUrl.Should().Be("https://api.monobank.ua");
        sp.GetRequiredService<IOptions<ExchangeRatesOptions>>().Value.NbuBaseUrl.Should().Be("https://bank.gov.ua/");
    }

    [Fact]
    public void TrueLayer_ClientIdWithoutSecret_Fails()
    {
        var sp = Build(new() { ["TrueLayer:ClientId"] = "id" });

        var act = () => sp.GetRequiredService<IOptions<TrueLayerOptions>>().Value;

        act.Should().Throw<OptionsValidationException>().WithMessage("*set together*");
    }

    [Fact]
    public void TrueLayer_BothCredentials_AreBound()
    {
        var sp = Build(new() { ["TrueLayer:ClientId"] = "id", ["TrueLayer:ClientSecret"] = "secret" });

        sp.GetRequiredService<IOptions<TrueLayerOptions>>().Value.IsConfigured.Should().BeTrue();
    }

    [Theory]
    [InlineData("TrueLayer:AuthBaseUrl", "not a url")]
    [InlineData("TrueLayer:CallbackPath", "no-leading-slash")]
    [InlineData("Binance:BaseUrl", "binance")]
    [InlineData("Binance:RecvWindowMs", "0")]
    [InlineData("Binance:DustThresholdUsd", "-1")]
    [InlineData("RevolutX:BaseUrl", "")]
    [InlineData("Monobank:BaseUrl", "nope")]
    [InlineData("ExchangeRates:NbuBaseUrl", "nope")]
    public void MalformedValue_Fails(string key, string value)
    {
        var sp = Build(new() { [key] = value });

        var act = () =>
        {
            _ = sp.GetRequiredService<IOptions<TrueLayerOptions>>().Value;
            _ = sp.GetRequiredService<IOptions<BinanceOptions>>().Value;
            _ = sp.GetRequiredService<IOptions<RevolutXOptions>>().Value;
            _ = sp.GetRequiredService<IOptions<MonobankOptions>>().Value;
            _ = sp.GetRequiredService<IOptions<ExchangeRatesOptions>>().Value;
        };

        act.Should().Throw<OptionsValidationException>();
    }
}
