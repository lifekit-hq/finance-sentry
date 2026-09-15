using FinanceSentry.Mcp.Abstractions;
using FinanceSentry.Mcp.Middleware;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FinanceSentry.Mcp;

// Explicit, namespaced entry point (not top-level statements) so this Exe does not occupy the global
// `Program` symbol — otherwise it collides with FinanceSentry.API's public `Program` in any test/host
// that transitively references both (e.g. the browser agent's tool bridge pulls this assembly in).
internal static class Program
{
    public static async Task Main(string[] args)
    {
        // MCP_TRANSPORT selects how this process speaks MCP:
        //   stdio (default) — JSON-RPC over stdin/stdout, used by Claude Desktop and the
        //                     ./mcp-probe.sh harness.
        //   http            — Streamable HTTP (ASP.NET Core), used by OpenClaw which can't
        //                     spawn arbitrary docker-exec processes from inside the gateway
        //                     container.
        var transport = (Environment.GetEnvironmentVariable("MCP_TRANSPORT") ?? "stdio").Trim().ToLowerInvariant();

        var bootstrapLoggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var cliExitCode = await McpAuthCli.TryRunAsync(args, new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .Build(), bootstrapLoggerFactory);
        if (cliExitCode.HasValue)
        {
            Environment.Exit(cliExitCode.Value);
        }

        // The canonical module list + tool assembly live in McpServiceRegistration so the host and the
        // DI-resolution test build the identical graph (feature 035).
        var mcpAssembly = McpServiceRegistration.McpAssembly;

        if (transport is "stdio")
        {
            var builder = Host.CreateApplicationBuilder(args);

            // stdio MCP framing: stdout carries JSON-RPC only — push all log output to stderr.
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

            McpServiceRegistration.RegisterShared(builder.Services, builder.Configuration);
            builder.Services.AddHostedService<LocalMcpSessionRefreshService>();

            builder.Services
                .AddMcpServer()
                .WithStdioServerTransport()
                .WithFinanceSentryTools(mcpAssembly);

            var host = builder.Build();
            await host.Services.GetRequiredService<LocalMcpSession>().InitializeAsync();
            await host.RunAsync();
        }
        else if (transport is "http" or "streamable-http")
        {
            var builder = WebApplication.CreateBuilder(args);

            // HTTP transport — logs can go to stdout freely.
            McpServiceRegistration.RegisterShared(builder.Services, builder.Configuration);

            builder.Services
                .AddMcpServer()
                // Stateless: each tool-call POST is handled inline within its HTTP request, so the
                // authenticated HttpContext (and thus per-request identity) flows to the tool. With
                // stateful sessions the tool runs on a background loop where HttpContext is null.
                .WithHttpTransport(o => o.Stateless = true)
                .WithFinanceSentryTools(mcpAssembly);

            var port = int.TryParse(Environment.GetEnvironmentVariable("MCP_HTTP_PORT"), out var p) ? p : 5100;
            builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

            var app = builder.Build();
            app.UseMiddleware<McpJwtAuthenticationMiddleware>();
            app.MapMcp();
            await app.RunAsync();
        }
        else
        {
            Console.Error.WriteLine($"Unknown MCP_TRANSPORT={transport}. Use 'stdio' or 'http'.");
            Environment.Exit(2);
        }
    }
}
