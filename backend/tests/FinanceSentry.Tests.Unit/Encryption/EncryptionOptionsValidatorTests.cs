namespace FinanceSentry.Tests.Unit.Encryption;

using FinanceSentry.Infrastructure.Encryption;
using FluentAssertions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

/// <summary>
/// The key-loading path (issue #493).
///
/// Production ran for months with no <c>Encryption:Keys</c> configured, and the service quietly
/// substituted a key committed to a public repository — so every stored bank credential was
/// readable by anyone who could read the repo and the database. Nothing failed, nothing warned.
/// These tests pin the two rules that replace that silence: outside Development an unconfigured or
/// disclosed key is a startup failure, and in Development the fallback is announced rather than
/// silent.
/// </summary>
public class EncryptionOptionsValidatorTests
{
    private const string ValidKeyBase64 = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY="; // 32 bytes

    private static EncryptionOptionsValidator ValidatorFor(string environmentName) =>
        new(new StubHostEnvironment(environmentName), NullLogger<EncryptionOptionsValidator>.Instance);

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void NoKeyConfigured_OutsideDevelopment_FailsStartup(string environmentName)
    {
        var options = new EncryptionOptions { Keys = [] };

        var result = ValidatorFor(environmentName).Validate(name: null, options);

        result.Failed.Should().BeTrue(
            "an unconfigured key outside Development is how #493 happened — the process must not start");
        result.FailureMessage.Should().Contain("Encryption__Keys__1");
    }

    [Fact]
    public void NoKeyConfigured_InDevelopment_FallsBackButIsAnnounced()
    {
        var options = new EncryptionOptions { Keys = [] };

        var result = ValidatorFor("Development").Validate(name: null, options);

        result.Succeeded.Should().BeTrue("a local checkout must still run");
        options.Keys.Should().ContainValue(CredentialEncryptionService.DisclosedFallbackKeyBase64);
        options.CurrentKeyVersion.Should().Be(1);
    }

    [Fact]
    public void DisclosedKeyAsTheCurrentKey_OutsideDevelopment_FailsStartup()
    {
        // The disclosed key pasted into a production config is the same disclosure as the removed
        // fallback, so it is refused on its VALUE rather than on where it came from — when it is
        // the key new credentials would be WRITTEN under.
        var options = new EncryptionOptions
        {
            CurrentKeyVersion = 1,
            Keys = new Dictionary<int, string>
            {
                [1] = CredentialEncryptionService.DisclosedFallbackKeyBase64,
            },
        };

        var result = ValidatorFor("Production").Validate(name: null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("compromised");
    }

    [Fact]
    public void DisclosedKeyAsALegacyVersion_OutsideDevelopment_IsAllowedSoRotationCanRun()
    {
        // THE migration this change exists to enable: every production row is at version 1 under
        // the disclosed key, so it must stay configured — read-only, non-current — until startup
        // rotation has moved every row to version 2. Refusing it here would make the disclosure
        // permanent, which is the opposite of the goal.
        var options = new EncryptionOptions
        {
            CurrentKeyVersion = 2,
            Keys = new Dictionary<int, string>
            {
                [1] = CredentialEncryptionService.DisclosedFallbackKeyBase64,
                [2] = ValidKeyBase64,
            },
        };

        ValidatorFor("Production").Validate(name: null, options).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void ValidKeyConfigured_OutsideDevelopment_Succeeds()
    {
        var options = new EncryptionOptions
        {
            CurrentKeyVersion = 1,
            Keys = new Dictionary<int, string> { [1] = ValidKeyBase64 },
        };

        ValidatorFor("Production").Validate(name: null, options).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void CurrentKeyVersionWithNoMatchingKey_FailsStartup()
    {
        // Rotation bumps CurrentKeyVersion; forgetting to ship the key alongside it would leave the
        // app unable to encrypt anything new, discovered at the first credential write instead.
        var options = new EncryptionOptions
        {
            CurrentKeyVersion = 2,
            Keys = new Dictionary<int, string> { [1] = ValidKeyBase64 },
        };

        var result = ValidatorFor("Production").Validate(name: null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("CurrentKeyVersion");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bm90LTMyLWJ5dGVz")] // valid Base64, wrong length
    [InlineData("not-base64-at-all!!")]
    public void UnusableKey_FailsStartup(string key)
    {
        // `Encryption__Keys__1: ${ENCRYPTION_KEY_V1}` expands to an EMPTY value when the variable
        // is unset. That looks configured to a presence check and blows up at the first credential
        // read — the same late discovery this change exists to remove.
        var options = new EncryptionOptions
        {
            CurrentKeyVersion = 1,
            Keys = new Dictionary<int, string> { [1] = key },
        };

        ValidatorFor("Production").Validate(name: null, options).Failed.Should().BeTrue();
    }

    [Fact]
    public void ServiceItself_NoLongerSubstitutesTheDisclosedKey()
    {
        // The validator is the startup gate; this is the belt-and-braces one. Even constructed
        // directly with empty options — as a test or a stray composition root might — the service
        // must refuse rather than reach for the published key.
        var act = () => new CredentialEncryptionService(
            Microsoft.Extensions.Options.Options.Create(new EncryptionOptions { Keys = [] }));

        act.Should().Throw<InvalidOperationException>();
    }

    private sealed class StubHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "FinanceSentry.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } =
            new NullFileProvider();
    }
}
