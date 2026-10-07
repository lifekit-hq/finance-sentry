using System.Net;
using System.Text;
using System.Text.Json;
using FinanceSentry.Infrastructure.Encryption;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.Inzhur;

namespace FinanceSentry.Tests.Unit.BrokerageSync.Inzhur;

// Fakes of the Inzhur cabinet built from the shapes observed in the design report (data/fs-inzhur-sync-design). Every
// value is a placeholder: no phone number, password, code or token of a real account appears in these tests.
internal static class InzhurFakes
{
    public const string Phone = "000000000000";
    public const string Password = "placeholder-password";
    public const string AccessToken = "fake-access-token";
    public const string RefreshCookieName = "refresh_token";
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
            details = new { certificatesOwnedQuantity = owned, totalAmountUAH = totalUah, investedUAH = investedUah },
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
        => new { availableBalanceUAH = available, blockedBalanceUAH = blocked, bonusBalanceUAH = bonus, totalBalanceUAH = available + blocked + bonus };
}

/// <summary>Answers each request from a queue of canned responses and records what was sent.</summary>
internal sealed class FakeInzhurHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();

    public List<RecordedRequest> Requests { get; } = [];

    public FakeInzhurHandler Then(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        _responses.Enqueue(respond);
        return this;
    }

    public FakeInzhurHandler Then(HttpResponseMessage response) => Then(_ => response);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new RecordedRequest(
            request.Method,
            request.RequestUri!,
            request.Headers.Authorization?.ToString(),
            request.Headers.TryGetValues("Cookie", out var cookies) ? string.Join("; ", cookies) : null,
            body));

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
