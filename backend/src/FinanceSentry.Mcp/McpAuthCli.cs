using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Net;
using FinanceSentry.Mcp.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinanceSentry.Mcp;

internal static class McpAuthCli
{
    public static async Task<int?> TryRunAsync(string[] args, IConfiguration configuration, ILoggerFactory loggerFactory)
    {
        if (args.Length < 2 || !string.Equals(args[0], "auth", StringComparison.OrdinalIgnoreCase))
            return null;

        var command = args[1].ToLowerInvariant();
        var logger = loggerFactory.CreateLogger("FinanceSentry.Mcp.AuthCli");
        var options = configuration.GetSection(McpOptions.SectionName).Get<McpOptions>() ?? new McpOptions();
        Validator.ValidateObject(options, new ValidationContext(options), validateAllProperties: true);
        var store = new LocalMcpCredentialStore(Options.Create(options));
        await using var services = new ServiceCollection()
            .AddSingleton(loggerFactory)
            .AddLogging()
            .AddMcpOAuthTokenClient()
            .BuildServiceProvider();
        var tokenClient = services.GetRequiredService<McpOAuthTokenClient>();

        return command switch
        {
            "login" => await RunLoginAsync(args.Skip(2).ToArray(), options, store, tokenClient, logger),
            "logout" => await RunLogoutAsync(store, tokenClient, logger),
            "status" => RunStatus(store),
            _ => 2
        };
    }

    private static async Task<int> RunLoginAsync(
        string[] args,
        McpOptions options,
        LocalMcpCredentialStore store,
        McpOAuthTokenClient tokenClient,
        ILogger logger)
    {
        var apiBaseUrl = GetOption(args, "--api-base")
            ?? options.ApiBaseUrl
            ?? "http://localhost:5001/api/v1";
        var frontendBaseUrl = GetOption(args, "--frontend-base")
            ?? options.FrontendBaseUrl
            ?? "http://localhost:4200";

        using var listener = StartListener(out var redirectUri);
        var state = Guid.NewGuid().ToString("N");
        var connectUrl = $"{frontendBaseUrl.TrimEnd('/')}/mcp/connect?redirectUri={Uri.EscapeDataString(redirectUri)}&state={Uri.EscapeDataString(state)}";

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = connectUrl,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to open browser automatically.");
            Console.Error.WriteLine($"Open this URL in your browser to authenticate MCP:\n{connectUrl}");
        }

        Console.Error.WriteLine("Waiting for MCP authorization callback...");
        var context = await listener.GetContextAsync().WaitAsync(TimeSpan.FromMinutes(5));
        var code = context.Request.QueryString["code"];
        var callbackState = context.Request.QueryString["state"];
        await WriteBrowserResponseAsync(context.Response, !string.IsNullOrWhiteSpace(code) && state == callbackState);

        if (string.IsNullOrWhiteSpace(code) || callbackState != state)
            return 1;

        var credentials = await tokenClient.ExchangeAuthorizationCodeAsync(apiBaseUrl, frontendBaseUrl, code, redirectUri);
        store.Save(credentials);

        Console.Error.WriteLine($"MCP login complete for {credentials.Email}. Credentials stored at {store.FilePath}.");
        return 0;
    }

    private static async Task<int> RunLogoutAsync(
        LocalMcpCredentialStore store,
        McpOAuthTokenClient tokenClient,
        ILogger logger)
    {
        var credentials = store.Load();
        if (credentials is not null)
            await tokenClient.RevokeAsync(credentials);

        store.Delete();
        logger.LogInformation("Removed local MCP credentials.");
        return 0;
    }

    private static int RunStatus(LocalMcpCredentialStore store)
    {
        var credentials = store.Load();
        if (credentials is null)
        {
            Console.Error.WriteLine("No local MCP OAuth credentials found.");
            return 1;
        }

        Console.Error.WriteLine($"User: {credentials.Email} ({credentials.UserId})");
        Console.Error.WriteLine($"Expires: {credentials.ExpiresAt:u}");
        Console.Error.WriteLine($"Credential file: {store.FilePath}");
        return 0;
    }

    private static HttpListener StartListener(out string redirectUri)
    {
        var port = GetFreePort();
        redirectUri = $"http://127.0.0.1:{port}/callback/";
        var listener = new HttpListener();
        listener.Prefixes.Add(redirectUri);
        listener.Start();
        return listener;
    }

    private static int GetFreePort()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task WriteBrowserResponseAsync(HttpListenerResponse response, bool success)
    {
        response.ContentType = "text/html; charset=utf-8";
        var html = success
            ? "<html><body><h2>Finance Sentry MCP connected.</h2><p>You may close this window.</p></body></html>"
            : "<html><body><h2>Finance Sentry MCP login failed.</h2><p>You may close this window and try again.</p></body></html>";
        await using var writer = new StreamWriter(response.OutputStream);
        await writer.WriteAsync(html);
    }

    private static string? GetOption(string[] args, string name)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
                return args[index + 1];
        }

        return null;
    }
}
