namespace FinanceSentry.Tests.Unit.Configuration;

using System.ComponentModel.DataAnnotations;
using FinanceSentry.Infrastructure.Observability;
using FinanceSentry.Mcp.Abstractions;
using FinanceSentry.Modules.Auth.Infrastructure.Authorization;
using FinanceSentry.Modules.BankSync.Application.Services;
using FinanceSentry.Modules.BankSync.Infrastructure.FeatureFlags;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

public class RemainingOptionsValidationTests
{
    private static IConfiguration Config(Dictionary<string, string?> settings) =>
        new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

    private static IServiceProvider Build(Dictionary<string, string?> settings, Action<IServiceCollection> register)
    {
        var services = new ServiceCollection();
        services.AddSingleton(Config(settings));
        register(services);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Deduplication_MissingKey_Fails()
    {
        var sp = Build([], s => s.AddOptions<DeduplicationOptions>()
            .BindConfiguration(DeduplicationOptions.SectionName).ValidateDataAnnotations());

        var act = () => sp.GetRequiredService<IOptions<DeduplicationOptions>>().Value;

        act.Should().Throw<OptionsValidationException>().WithMessage("*Deduplication:MasterKeyBase64*");
    }

    [Fact]
    public void Deduplication_KeySet_IsBound()
    {
        var sp = Build(new() { ["Deduplication:MasterKeyBase64"] = "a2V5" }, s => s.AddOptions<DeduplicationOptions>()
            .BindConfiguration(DeduplicationOptions.SectionName).ValidateDataAnnotations());

        sp.GetRequiredService<IOptions<DeduplicationOptions>>().Value.MasterKeyBase64.Should().Be("a2V5");
    }

    [Fact]
    public void AuthSeed_AbsentSettings_Start()
    {
        var sp = Build([], s => s.AddOptions<AuthSeedOptions>().BindConfiguration("Auth").ValidateDataAnnotations());

        var value = sp.GetRequiredService<IOptions<AuthSeedOptions>>().Value;

        value.OwnerEmail.Should().BeNull();
        value.SmokeAccount.Email.Should().BeNull();
    }

    [Fact]
    public void AuthSeed_KeepsTheExistingKeyNames()
    {
        var sp = Build(
            new() { ["Auth:OwnerEmail"] = "owner@example.com", ["Auth:SmokeAccount:Email"] = "smoke@example.com" },
            s => s.AddOptions<AuthSeedOptions>().BindConfiguration("Auth").ValidateDataAnnotations());

        var value = sp.GetRequiredService<IOptions<AuthSeedOptions>>().Value;

        value.OwnerEmail.Should().Be("owner@example.com");
        value.SmokeAccount.Email.Should().Be("smoke@example.com");
    }

    [Fact]
    public void AuthSeed_MalformedOwnerEmail_Fails()
    {
        var sp = Build(new() { ["Auth:OwnerEmail"] = "not-an-email" },
            s => s.AddOptions<AuthSeedOptions>().BindConfiguration("Auth").ValidateDataAnnotations());

        var act = () => sp.GetRequiredService<IOptions<AuthSeedOptions>>().Value;

        act.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public void AuthSeed_MalformedSmokeEmail_Fails()
    {
        var sp = Build(new() { ["Auth:SmokeAccount:Email"] = "not-an-email" },
            s => s.AddOptions<AuthSeedOptions>().BindConfiguration("Auth").ValidateDataAnnotations());

        var act = () => sp.GetRequiredService<IOptions<AuthSeedOptions>>().Value;

        act.Should().Throw<OptionsValidationException>().WithMessage("*Auth:SmokeAccount:Email*");
    }

    [Fact]
    public void AuthSeed_EmptySmokeEmail_Starts()
    {
        var sp = Build(new() { ["Auth:SmokeAccount:Email"] = string.Empty },
            s => s.AddOptions<AuthSeedOptions>().BindConfiguration("Auth").ValidateDataAnnotations());

        sp.GetRequiredService<IOptions<AuthSeedOptions>>().Value.SmokeAccount.Email.Should().BeNullOrEmpty();
    }

    [Fact]
    public void Mcp_AbsentSettings_Start()
    {
        var sp = Build([], s => s.AddOptions<McpOptions>().BindConfiguration("Mcp").ValidateDataAnnotations());

        sp.GetRequiredService<IOptions<McpOptions>>().Value.ApiBaseUrl.Should().BeNull();
    }

    [Fact]
    public void Mcp_MalformedUrl_Fails()
    {
        var sp = Build(new() { ["Mcp:ApiBaseUrl"] = "nope" },
            s => s.AddOptions<McpOptions>().BindConfiguration("Mcp").ValidateDataAnnotations());

        var act = () => sp.GetRequiredService<IOptions<McpOptions>>().Value;

        act.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public void FeatureFlags_BindCaseInsensitively()
    {
        var sp = Build(new() { ["FeatureFlags:Radar"] = "true" },
            s => s.AddOptions<FeatureFlagOptions>().BindConfiguration("FeatureFlags"));
        var service = new FeatureFlagService(sp.GetRequiredService<IOptionsMonitor<FeatureFlagOptions>>());

        service.IsEnabled("radar").Should().BeTrue();
        service.IsEnabled("missing").Should().BeFalse();
    }

    [Fact]
    public void DataProtection_NoCertificate_Starts()
    {
        var sp = Build([], s => s.AddOptions<FinanceSentry.Modules.Auth.Infrastructure.Authentication.DataProtectionCertificateOptions>()
            .BindConfiguration("DataProtection").ValidateDataAnnotations());

        sp.GetRequiredService<IOptions<FinanceSentry.Modules.Auth.Infrastructure.Authentication.DataProtectionCertificateOptions>>()
            .Value.TryLoad().Should().BeNull();
    }

    [Fact]
    public void DataProtection_GarbageCertificate_Fails()
    {
        var sp = Build(new() { ["DataProtection:CertificatePfxBase64"] = "bm90IGEgY2VydA==" },
            s => s.AddOptions<FinanceSentry.Modules.Auth.Infrastructure.Authentication.DataProtectionCertificateOptions>()
                .BindConfiguration("DataProtection").ValidateDataAnnotations());

        var act = () => sp.GetRequiredService<IOptions<FinanceSentry.Modules.Auth.Infrastructure.Authentication.DataProtectionCertificateOptions>>().Value;

        act.Should().Throw<OptionsValidationException>().WithMessage("*PKCS#12*");
    }

    [Fact]
    public void Observability_Unset_UsesTheCollectorDefault()
    {
        var options = ObservabilityOptions.From(Config([]));

        options.Otlp.Endpoint.Should().Be(ObservabilityOptions.DefaultOtlpEndpoint);
    }

    [Fact]
    public void Observability_ExplicitEmptyEndpoint_StaysEmpty()
    {
        ObservabilityOptions.From(Config(new() { ["Observability:Otlp:Endpoint"] = string.Empty }))
            .Otlp.Endpoint.Should().BeNullOrEmpty();
    }

    [Fact]
    public void Observability_MalformedUrl_Fails()
    {
        var act = () => ObservabilityOptions.From(Config(new() { ["Observability:Otlp:Endpoint"] = "nope" }));

        act.Should().Throw<ValidationException>();
    }
}
