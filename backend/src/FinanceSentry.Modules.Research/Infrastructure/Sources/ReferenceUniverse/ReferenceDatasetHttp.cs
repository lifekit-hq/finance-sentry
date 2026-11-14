namespace FinanceSentry.Modules.Research.Infrastructure.Sources.ReferenceUniverse;

using FinanceSentry.Modules.Research.Application.Services.ReferenceUniverse;

/// <summary>Shared download step of the CSV feeds: a failure becomes a <see cref="ReferenceFeedException"/> naming the URL.</summary>
internal static class ReferenceDatasetHttp
{
    public const string HttpClientName = "reference-universe";

    public static async Task<List<List<string>>> GetRowsAsync(
        IHttpClientFactory factory, string url, CancellationToken ct)
    {
        try
        {
            var client = factory.CreateClient(HttpClientName);
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream);
            return CsvReader.ReadRows(reader).ToList();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException
            && !ct.IsCancellationRequested)
        {
            throw new ReferenceFeedException($"download of {url} failed: {ex.Message}", ex);
        }
    }
}
