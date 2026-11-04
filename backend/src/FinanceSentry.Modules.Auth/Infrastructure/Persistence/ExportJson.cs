using System.Text.Json;

namespace FinanceSentry.Modules.Auth.Infrastructure.Persistence;

internal static class ExportJson
{
    public static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
}
