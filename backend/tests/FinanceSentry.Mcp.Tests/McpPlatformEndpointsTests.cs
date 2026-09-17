using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Xunit;

namespace FinanceSentry.Mcp.Tests;

/// <summary>
/// REST contract of the MCP host's anonymous platform endpoints (constitution § Testing Discipline;
/// spec 048 US3). Boots only <see cref="McpPlatformEndpoints"/> on a test server with an unreachable
/// Postgres, so readiness is exercised on its failure branch: the checker treats the status code and
/// content type as the contract and rejects <c>text/html</c>.
/// </summary>
public sealed class McpPlatformEndpointsTests : IAsyncLifetime
{
    // Nothing listens on port 1: the Npgsql check fails fast with a connection refusal.
    private const string UnreachableConnectionString = "Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=1";

    private WebApplication? _app;
    private HttpClient? _client;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = UnreachableConnectionString,
        });
        builder.Services.AddMcpPlatformEndpoints(builder.Configuration);

        _app = builder.Build();
        _app.MapMcpPlatformEndpoints();
        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_app is not null)
            await _app.DisposeAsync();
    }

    [Fact]
    public async Task Health_Is200Json()
    {
        var response = await _client!.GetAsync(McpPlatformEndpoints.HealthPath);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("status").GetString().Should().Be("healthy");
    }

    [Fact]
    public async Task Ready_WithUnreachableDatabase_Is503Json_NamingTheDatabaseCheck()
    {
        var response = await _client!.GetAsync(McpPlatformEndpoints.ReadyPath);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("status").GetString().Should().Be("Unhealthy");
        var database = body.RootElement.GetProperty("checks").EnumerateArray()
            .Should().ContainSingle(c => c.GetProperty("name").GetString() == "database").Subject;
        database.GetProperty("status").GetString().Should().Be("Unhealthy");
    }

    [Fact]
    public async Task Metrics_Is200PrometheusText()
    {
        var response = await _client!.GetAsync(McpPlatformEndpoints.MetricsPath);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/plain");
        (await response.Content.ReadAsStringAsync()).Should().Contain("target_info{service_name=\"finance-sentry-mcp\"");
    }
}
