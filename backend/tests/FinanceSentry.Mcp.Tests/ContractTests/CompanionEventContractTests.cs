using System.Text.Json;
using FinanceSentry.Modules.Companion.API.Responses;
using FluentAssertions;
using Xunit;

namespace FinanceSentry.Mcp.Tests.ContractTests;

/// <summary>
/// <c>get_pending_companion_events</c> is a shared contract the OpenClaw agent reads (#466 N-B): <c>appUrl</c> is
/// additive. Every field the agent already reads stays under its name, and a null link is absent, not <c>null</c>.
/// </summary>
public sealed class CompanionEventContractTests
{
    private static readonly string[] ExistingFields =
        ["Id", "Kind", "Subject", "Severity", "Summary", "ReferenceId", "Disposition", "OccurredAt"];

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static CompanionEventDto Dto(string? appUrl) => new(
        Guid.NewGuid(), "ThesisBroken", "NVDA", "critical", "summary", null, "Pending", DateTimeOffset.UtcNow, appUrl);

    [Fact]
    public void Dto_keeps_every_existing_field_and_adds_app_url()
        => typeof(CompanionEventDto).GetProperties().Select(p => p.Name)
            .Should().Contain(ExistingFields).And.Contain("AppUrl");

    [Fact]
    public void Serialised_event_names_the_link_appUrl_and_keeps_the_existing_keys()
    {
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(Dto("https://app.example.com/assets/NVDA"), Web));

        json.RootElement.GetProperty("appUrl").GetString().Should().Be("https://app.example.com/assets/NVDA");
        foreach (var field in ExistingFields)
        {
            json.RootElement.TryGetProperty(JsonNamingPolicy.CamelCase.ConvertName(field), out _)
                .Should().BeTrue($"{field} is part of the contract the agent already reads");
        }
    }

    [Fact]
    public void Serialised_event_without_a_link_omits_the_key()
    {
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(Dto(null), Web));

        json.RootElement.TryGetProperty("appUrl", out _).Should().BeFalse();
    }
}
