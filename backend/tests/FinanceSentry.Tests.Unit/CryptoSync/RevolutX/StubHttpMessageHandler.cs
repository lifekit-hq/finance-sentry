using System.Net;
using System.Text;

namespace FinanceSentry.Tests.Unit.CryptoSync.RevolutX;

/// <summary>Replays a canned body per request path and records every request it saw.</summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Dictionary<string, (HttpStatusCode Status, string Body)> _responses = new(StringComparer.Ordinal);

    public List<HttpRequestMessage> Requests { get; } = [];

    public Exception? Throw { get; set; }

    public StubHttpMessageHandler Respond(string path, string body, HttpStatusCode status = HttpStatusCode.OK)
    {
        _responses[path] = (status, body);
        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        if (Throw is not null)
        {
            throw Throw;
        }

        var path = request.RequestUri!.AbsolutePath;
        var (status, body) = _responses.TryGetValue(path, out var response)
            ? response
            : (HttpStatusCode.NotFound, "{\"message\":\"no stub\"}");

        return Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        });
    }
}

internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
