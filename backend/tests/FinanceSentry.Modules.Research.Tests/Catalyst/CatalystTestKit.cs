namespace FinanceSentry.Modules.Research.Tests.Catalyst;

using System.Net;
using FinanceSentry.Modules.Research.Application.Services.Catalyst;
using FinanceSentry.Modules.Research.Domain.Catalyst;

/// <summary>
/// A clock whose timers fire at once and move the clock forward by the delay they were asked for: <c>Task.Delay(wait, clock)</c>
/// returns immediately and <see cref="Now"/> shows how long a paced sequence would really have taken.
/// </summary>
internal sealed class AutoAdvanceClock(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Start { get; } = start;

    public DateTimeOffset Now { get; private set; } = start;

    public TimeSpan Elapsed => Now - Start;

    public override DateTimeOffset GetUtcNow() => Now;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        if (dueTime > TimeSpan.Zero)
        {
            Now += dueTime;
        }

        ThreadPool.QueueUserWorkItem(_ => callback(state));
        return new NoopTimer();
    }

    private sealed class NoopTimer : ITimer
    {
        public bool Change(TimeSpan dueTime, TimeSpan period) => true;

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

/// <summary>A full-text search the test scripts per item; <see cref="Fail"/> makes every call raise the feed exception.</summary>
internal sealed class ScriptedFullText : IEdgarFullTextSearch
{
    public Dictionary<string, List<CatalystFiling>> ByItem { get; } = [];

    public string? Fail { get; set; }

    public List<(string Item, DateOnly From, DateOnly To)> Calls { get; } = [];

    public Task<IReadOnlyList<CatalystFiling>> SearchAsync(string item, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        Calls.Add((item, from, to));
        if (Fail is not null)
        {
            throw new CatalystFeedException(Fail);
        }

        return Task.FromResult<IReadOnlyList<CatalystFiling>>(ByItem.TryGetValue(item, out var list) ? [.. list] : []);
    }
}

/// <summary>A GDELT client the test scripts per phrase; a phrase in <see cref="Failing"/> raises the feed exception.</summary>
internal sealed class ScriptedGdelt : IGdeltTimelineClient
{
    public Dictionary<string, IReadOnlyList<GdeltDay>> ByPhrase { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> Failing { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Asked { get; } = [];

    public Task<IReadOnlyList<GdeltDay>> GetDailyVolumeAsync(string phrase, int days, CancellationToken ct = default)
    {
        Asked.Add(phrase);
        if (Failing.Contains(phrase))
        {
            throw new CatalystFeedException($"GDELT throttled \"{phrase}\".");
        }

        return Task.FromResult(ByPhrase.TryGetValue(phrase, out var volume) ? volume : []);
    }
}

/// <summary>An <see cref="IHttpClientFactory"/> whose client answers every request through a scripted function and records the URLs.</summary>
internal sealed class ScriptedHttp(Func<string, (HttpStatusCode Status, string Body)> answer) : IHttpClientFactory
{
    private Func<string, (HttpStatusCode Status, string Body)> Answer { get; } = answer;

    public List<string> Requested { get; } = [];

    public HttpClient CreateClient(string name) => new(new Handler(this));

    private sealed class Handler(ScriptedHttp owner) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!.AbsoluteUri;
            owner.Requested.Add(url);
            var (status, body) = owner.Answer(url);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }
}

internal static class Filings
{
    public static CatalystFiling Of(
        string ticker, string accession, string items, DateOnly filed, string form = "8-K", string source = CatalystRules.SourceFullText)
        => new(ticker, accession, form, CatalystRules.ParseItems([items]), filed, $"https://sec.test/{accession}", source);
}
