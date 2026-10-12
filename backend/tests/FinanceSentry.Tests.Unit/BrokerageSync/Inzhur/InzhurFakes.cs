using System.Net;
using System.Text;
using System.Text.Json;
using FinanceSentry.Infrastructure.Encryption;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.Inzhur;

namespace FinanceSentry.Tests.Unit.BrokerageSync.Inzhur;

// Fakes of the Inzhur cabinet built from the shapes observed in the design report (data/fs-inzhur-sync-design) and the
// cabinet's own web client. Every value is a placeholder: no cookie, token, name or amount of a real account appears in
// these tests. Helpers take hryvnia and put kopecks on the wire, as the cabinet does for every money amount (prices
// stay in hryvnia).
internal static class InzhurFakes
{
    public const decimal Kopecks = 100m;

    public const string AccessToken = "fake-access-token";
    public const string RefreshCookieName = "refreshToken";
    public const string AuthHost = "api.inzhur.reit";

    public static InzhurSession Session(string accessToken = AccessToken, string refreshValue = "fake-refresh-1")
        => new(accessToken, [new InzhurCookie(RefreshCookieName, refreshValue, AuthHost, "/", null, Secure: true, HttpOnly: true)]);

    public static HttpResponseMessage Json(HttpStatusCode status, object body)
        => new(status) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };

    public static HttpResponseMessage Error(HttpStatusCode status, string message)
        => Json(status, new { error = new { message } });

    public static object UserAssets(params object[] assets) => new { assets };

    public static object Fund(string name, decimal owned, decimal totalUah, decimal investedUah, int id = 1)
        => new
        {
            id,
            type = "fund",
            status = "active",
            name,
            prices = new { sellUAH = totalUah / owned },
            details = new { certificatesOwnedQuantity = owned, totalAmountUAH = totalUah * Kopecks, investedUAH = investedUah * Kopecks },
        };

    public static object Bond(string isin, decimal quantity, decimal sellUah, int id = 2)
        => new
        {
            id,
            type = "bond",
            status = "active",
            isin,
            prices = new { sellUAH = sellUah },
            securityProperties = new { availableQuantity = quantity },
        };

    public static object BrokerAccount(decimal available, decimal blocked, decimal bonus = 0m)
        => new
        {
            availableBalanceUAH = available * Kopecks,
            blockedBalanceUAH = blocked * Kopecks,
            bonusBalanceUAH = bonus * Kopecks,
            totalBalanceUAH = (available + blocked + bonus) * Kopecks,
        };

    public static object Catalog(params object[] items) => new { items };

    public static object CatalogEntry(int assetId, string name, string type = "fund", decimal sellUah = 0m)
        => new { assetId, type, name, status = "active", prices = new { sellUAH = sellUah } };

    /// <summary>A ledger row; <paramref name="amountUah"/> goes out unsigned in kopecks, as the cabinet sends it.</summary>
    public static object Transaction(
        string id, string type, decimal amountUah, string at, string? assetName = null, decimal? quantity = null,
        string? distributionType = null, string assetType = "fund", string? isin = null)
        => new
        {
            id,
            type,
            amount = amountUah * Kopecks,
            externalCreatedAt = at,
            distributionType,
            asset = assetName is null && isin is null ? null : new { type = assetType, name = assetName, isin },
            contract = quantity is null ? null : new { quantity },
        };
}

/// <summary>Answers each request from a queue of canned responses and records what was sent.</summary>
internal sealed class FakeInzhurHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();
    private readonly Dictionary<string, Queue<Func<HttpRequestMessage, HttpResponseMessage>>> _routes = new(StringComparer.Ordinal);

    public List<RecordedRequest> Requests { get; } = [];

    public FakeInzhurHandler Then(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        _responses.Enqueue(respond);
        return this;
    }

    public FakeInzhurHandler Then(HttpResponseMessage response) => Then(_ => response);

    /// <summary>Answers the next call to <paramref name="absolutePath"/> ahead of the ordered queue.</summary>
    public FakeInzhurHandler On(string absolutePath, HttpResponseMessage response)
    {
        if (!_routes.TryGetValue(absolutePath, out var queue))
            _routes[absolutePath] = queue = new();
        queue.Enqueue(_ => response);
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new RecordedRequest(
            request.Method,
            request.RequestUri!,
            request.Headers.Authorization?.ToString(),
            request.Headers.TryGetValues("Cookie", out var cookies) ? string.Join("; ", cookies) : null,
            body));

        if (_routes.TryGetValue(request.RequestUri!.AbsolutePath, out var routed) && routed.Count > 0)
            return routed.Dequeue()(request);
        if (_responses.Count == 0)
            throw new InvalidOperationException($"Unexpected Inzhur call {request.Method} {request.RequestUri}.");
        return _responses.Dequeue()(request);
    }
}

internal sealed record RecordedRequest(HttpMethod Method, Uri Uri, string? Authorization, string? Cookie, string? Body);

/// <summary>Reversible stand-in for AES-GCM: the "ciphertext" is the UTF-8 plaintext, under a fixed key version.</summary>
internal sealed class FakeEncryption(int keyVersion = 1) : ICredentialEncryptionService
{
    public EncryptionResult Encrypt(string plaintext) => new(Encoding.UTF8.GetBytes(plaintext), [1], [2], keyVersion);

    public string Decrypt(byte[] ciphertext, byte[] iv, byte[] authTag, int keyVersion) => Encoding.UTF8.GetString(ciphertext);
}

/// <summary>A clock tests move by hand.</summary>
internal sealed class ManualClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}
