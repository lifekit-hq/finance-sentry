using FinanceSentry.Core.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FinanceSentry.Mcp.Abstractions;

/// <summary>
/// The <c>Mcp</c> configuration section. All optional: an unset base URL falls back to the local-dev origin and an
/// unset <see cref="CredentialFile"/> to <c>FINANCE_SENTRY_MCP_AUTH_FILE</c>, then the per-user config directory.
/// Whatever is set must be a well-formed URL.
/// </summary>
public sealed class McpOptions
{
    public const string SectionName = "Mcp";

    [OptionalUrl]
    public string? ApiBaseUrl { get; set; }

    [OptionalUrl]
    public string? FrontendBaseUrl { get; set; }

    public string? CredentialFile { get; set; }
}

public static class McpOptionsServiceCollectionExtensions
{
    /// <summary>Binds <see cref="McpOptions"/> and registers the credential store that reads it.</summary>
    public static IServiceCollection AddLocalMcpCredentialStore(this IServiceCollection services)
    {
        services.AddOptions<McpOptions>()
            .BindConfiguration(McpOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<LocalMcpCredentialStore>();
        return services;
    }
}
