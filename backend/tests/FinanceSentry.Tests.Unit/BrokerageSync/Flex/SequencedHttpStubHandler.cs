using System.Net;
using System.Text;

namespace FinanceSentry.Tests.Unit.BrokerageSync.Flex;

/// <summary>
/// Replays a queue of canned XML bodies per request path, in order — one dequeued per matching
/// request. Needed for the Flex client tests: unlike the RevolutX <c>StubHttpMessageHandler</c>
/// (one fixed body per path), the same <c>GetStatement</c> path here must answer "not yet
/// generated" on the first poll(s) and the real statement once it is "ready".
/// </summary>
internal sealed class SequencedHttpStubHandler : HttpMessageHandler
{
    private readonly Dictionary<string, Queue<(HttpStatusCode Status, string Body)>> _responses = new(StringComparer.Ordinal);

    public List<Uri> RequestUris { get; } = [];

    public SequencedHttpStubHandler Enqueue(string path, string body, HttpStatusCode status = HttpStatusCode.OK)
    {
        if (!_responses.TryGetValue(path, out var queue))
            _responses[path] = queue = new Queue<(HttpStatusCode, string)>();

        queue.Enqueue((status, body));
        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequestUris.Add(request.RequestUri!);

        var path = request.RequestUri!.AbsolutePath;
        if (!_responses.TryGetValue(path, out var queue) || queue.Count == 0)
            throw new InvalidOperationException($"No stubbed response left for {path}.");

        var (status, body) = queue.Count > 1 ? queue.Dequeue() : queue.Peek();

        return Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "text/xml"),
        });
    }
}
