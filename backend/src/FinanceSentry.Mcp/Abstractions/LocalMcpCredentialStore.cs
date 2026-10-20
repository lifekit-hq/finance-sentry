using System.Text.Json;
using Microsoft.Extensions.Options;

namespace FinanceSentry.Mcp.Abstractions;

public sealed class LocalMcpCredentialStore(IOptions<McpOptions> options)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public string FilePath => options.Value.CredentialFile
        ?? Environment.GetEnvironmentVariable("FINANCE_SENTRY_MCP_AUTH_FILE")
        ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".config",
            "finance-sentry",
            "mcp-auth.json");

    public StoredMcpCredentials? Load()
    {
        if (!File.Exists(FilePath))
            return null;

        return JsonSerializer.Deserialize<StoredMcpCredentials>(File.ReadAllText(FilePath), JsonOptions);
    }

    public void Save(StoredMcpCredentials credentials)
    {
        var directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(FilePath, JsonSerializer.Serialize(credentials, JsonOptions));
    }

    public void Delete()
    {
        if (File.Exists(FilePath))
            File.Delete(FilePath);
    }
}
