using System.ComponentModel;
using ModelContextProtocol.Server;

namespace FinanceSentry.Mcp.Resources;

/// <summary>
/// The framework primer as an MCP resource: the short map of what the server's numbers mean (the ten
/// measures, the headline contract, flow classes, decided calls, where each figure comes from), so a
/// client reads it before reasoning about a number. The text is the hand-written
/// <c>finance-framework-primer.md</c> embedded in this assembly, deployed with the numbers it describes;
/// <c>FinanceFrameworkPrimerContractTests</c> pins its anchors to <c>docs/finance-framework.md</c>.
/// </summary>
[McpServerResourceType]
public sealed class FinanceFrameworkPrimerResource
{
    public const string Uri = "finance-framework://primer";

    private const string EmbeddedName = "FinanceSentry.Mcp.Resources.finance-framework-primer.md";

    private static readonly Lazy<string> Text = new(Load);

    [McpServerResource(UriTemplate = Uri, Name = "finance_framework_primer", Title = "Finance framework primer", MimeType = "text/markdown")]
    [Description("Read before reasoning about any number from this server: what Finance Sentry is, the ten measures, the headline contract, flow classes and liability signs, valuation and currency, decided and open calls, and which tool each figure comes from.")]
    public static string Read() => Text.Value;

    private static string Load()
    {
        using var stream = typeof(FinanceFrameworkPrimerResource).Assembly.GetManifestResourceStream(EmbeddedName)
            ?? throw new InvalidOperationException($"Embedded resource '{EmbeddedName}' is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
