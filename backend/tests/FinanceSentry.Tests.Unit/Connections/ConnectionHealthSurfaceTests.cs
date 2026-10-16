namespace FinanceSentry.Tests.Unit.Connections;

using FinanceSentry.Core.Connections;
using FluentAssertions;
using Xunit;

/// <summary>What a person is shown of a connection's health (slice S5), for a provider the policy drives and one in shadow.</summary>
public class ConnectionHealthSurfaceTests
{
    [Theory]
    [InlineData(ConnectionHealthState.Healthy, null)]
    [InlineData(ConnectionHealthState.Degraded, null)]
    [InlineData(ConnectionHealthState.Failing, "retrying")]
    [InlineData(ConnectionHealthState.ActionRequired, "reconnect_needed")]
    [InlineData(ConnectionHealthState.Dormant, "dormant")]
    public void Each_state_has_its_label(ConnectionHealthState state, string? label) =>
        ConnectionHealthSurface.StatusFor("ibkr", state, HealthOptions.With("ibkr")).Should().Be(label);

    [Theory]
    [InlineData(ConnectionHealthState.Failing)]
    [InlineData(ConnectionHealthState.ActionRequired)]
    [InlineData(ConnectionHealthState.Dormant)]
    public void A_shadow_provider_keeps_its_own_status_whatever_its_health(ConnectionHealthState state) =>
        ConnectionHealthSurface.StatusFor("monobank", state, HealthOptions.With("ibkr")).Should().BeNull();

    [Fact]
    public void With_no_provider_listed_nothing_is_labelled() =>
        ConnectionHealthSurface.StatusFor("ibkr", ConnectionHealthState.ActionRequired, HealthOptions.With()).Should().BeNull();

    [Fact]
    public void An_unknown_health_keeps_the_path_status() =>
        ConnectionHealthSurface.StatusFor("ibkr", null, HealthOptions.With("ibkr")).Should().BeNull();

    [Theory]
    [InlineData(ConnectionHealthState.ActionRequired, ConnectionHealthState.Healthy, ConnectionHealthState.ActionRequired)]
    [InlineData(ConnectionHealthState.Failing, ConnectionHealthState.Dormant, ConnectionHealthState.Failing)]
    [InlineData(ConnectionHealthState.Healthy, ConnectionHealthState.Dormant, ConnectionHealthState.Dormant)]
    [InlineData(ConnectionHealthState.Healthy, ConnectionHealthState.Degraded, ConnectionHealthState.Degraded)]
    public void An_account_shows_its_connection_while_that_is_not_Healthy_else_its_own_state(
        ConnectionHealthState connection, ConnectionHealthState account, ConnectionHealthState shown) =>
        ConnectionHealthSurface.Effective(connection, account).Should().Be(shown);

    [Fact]
    public void An_account_with_no_connection_shows_its_own_state() =>
        ConnectionHealthSurface.Effective(null, ConnectionHealthState.Dormant).Should().Be(ConnectionHealthState.Dormant);

    [Theory]
    [InlineData("ibkr-flex", "ibkr")]
    [InlineData("ibkr", "ibkr")]
    [InlineData("monobank", "monobank")]
    public void A_provider_is_shown_under_its_institution(string provider, string institution) =>
        ConnectionHealthSurface.InstitutionOf(provider).Should().Be(institution);

    [Fact]
    public void A_group_shows_its_most_urgent_member() =>
        ConnectionHealthSurface.Worst(
            [ConnectionHealthState.Healthy, ConnectionHealthState.Degraded, ConnectionHealthState.ActionRequired, ConnectionHealthState.Failing])
            .Should().Be(ConnectionHealthState.ActionRequired);

    [Fact]
    public void One_Dormant_account_stays_quiet_beside_working_siblings() =>
        ConnectionHealthSurface.Worst([ConnectionHealthState.Dormant, ConnectionHealthState.Healthy])
            .Should().Be(ConnectionHealthState.Healthy);

    [Fact]
    public void A_group_is_Dormant_only_when_every_member_is() =>
        ConnectionHealthSurface.Worst([ConnectionHealthState.Dormant, ConnectionHealthState.Dormant])
            .Should().Be(ConnectionHealthState.Dormant);

    [Fact]
    public void An_empty_group_has_no_state() =>
        ConnectionHealthSurface.Worst([]).Should().BeNull();
}
