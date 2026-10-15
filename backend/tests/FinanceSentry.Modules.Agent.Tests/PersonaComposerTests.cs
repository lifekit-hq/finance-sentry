namespace FinanceSentry.Modules.Agent.Tests;

using FinanceSentry.Modules.Agent.Application.Services;
using FinanceSentry.Modules.Agent.Tests.Support;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

public sealed class PersonaComposerTests
{
    // Phrases unique to the OpenClaw adapter (Kit/sessions/cron mechanics) — must NOT leak into the
    // browser system prompt. The browser adapter may negate "Kit"/"cron", so these are chosen to appear
    // only in adapters/openclaw.md.
    private static readonly string[] OpenClawOnlyMarkers =
    [
        "A2A envelope",
        "reply_expected",
        "openclaw-openclaw-gateway",
        "ledger-lit-digest",
        "Kit routes tasks",
    ];

    private static PersonaComposer Composer() =>
        new(Options.Create(new AgentOptions { PersonaRootPath = RepoPaths.RepoRoot() }), NullLogger<PersonaComposer>.Instance);

    [Fact]
    public void Compose_IncludesCore_Browser_AndUser()
    {
        var prompt = Composer().Compose();

        // Core identity + laws.
        prompt.Should().Contain("Ledger");
        prompt.Should().Contain("Answer shape");
        prompt.Should().Contain("Three-layer");
        prompt.Should().Contain("Stay-invested");

        // Browser adapter surface.
        prompt.Should().Contain("Finance Sentry web app");

        // User profile.
        prompt.Should().Contain("Denys Sychov");
    }

    [Fact]
    public void Compose_DoesNotLeakOpenClawMechanics()
    {
        var prompt = Composer().Compose();

        foreach (var marker in OpenClawOnlyMarkers)
        {
            prompt.Should().NotContain(marker, $"'{marker}' is OpenClaw-only and must not appear in the browser persona");
        }
    }

    [Fact]
    public void Compose_IsCached_ReturnsSameInstanceText()
    {
        var composer = Composer();
        var first = composer.Compose();
        var second = composer.Compose();

        second.Should().BeSameAs(first);
    }
}
