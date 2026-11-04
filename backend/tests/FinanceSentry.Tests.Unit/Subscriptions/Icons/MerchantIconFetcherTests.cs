namespace FinanceSentry.Tests.Unit.Subscriptions.Icons;

using System.Net;
using FinanceSentry.Modules.Subscriptions.Infrastructure.Icons;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

public class MerchantIconFetcherTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 1, 2, 3];

    [Fact]
    public async Task Fetch_ImageResponse_ReturnsBytesAndType_FromFaviconPathOverHttps()
    {
        var handler = new StubHandler(_ => Image("image/png", Png));

        var icon = await Fetcher(handler).FetchAsync("www.example.com", CancellationToken.None);

        icon.Should().NotBeNull();
        icon!.ContentType.Should().Be("image/png");
        icon.Content.Should().Equal(Png);
        handler.Requests.Should().ContainSingle().Which.Should().Be(new Uri("https://www.example.com/favicon.ico"));
    }

    [Theory]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    [InlineData("169.254.169.254")]
    [InlineData("example.com:8443")]
    [InlineData("example.com/other")]
    [InlineData("user@example.com")]
    public async Task Fetch_HostIsNotAPlainDnsName_IsRefusedWithoutAnyRequest(string host)
    {
        var handler = new StubHandler(_ => Image("image/png", Png));

        var icon = await Fetcher(handler).FetchAsync(host, CancellationToken.None);

        icon.Should().BeNull();
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Fetch_RedirectWithinSameHost_IsFollowed()
    {
        var handler = new StubHandler(uri => uri.AbsolutePath == "/favicon.ico"
            ? Redirect("https://www.example.com/static/icon.png")
            : Image("image/png", Png));

        var icon = await Fetcher(handler).FetchAsync("www.example.com", CancellationToken.None);

        icon.Should().NotBeNull();
        handler.Requests.Should().HaveCount(2);
    }

    [Theory]
    [InlineData("https://other.example.net/favicon.ico")]
    [InlineData("https://cdn.example.com/favicon.ico")]
    [InlineData("http://www.example.com/favicon.ico")]
    [InlineData("https://www.example.com:8443/favicon.ico")]
    [InlineData("http://169.254.169.254/latest/meta-data")]
    public async Task Fetch_RedirectToAnotherHostSchemeOrPort_IsRefused(string target)
    {
        var handler = new StubHandler(_ => Redirect(target));

        var icon = await Fetcher(handler).FetchAsync("www.example.com", CancellationToken.None);

        icon.Should().BeNull();
        handler.Requests.Should().ContainSingle("the redirect target is never requested");
    }

    [Fact]
    public async Task Fetch_RedirectLoop_GivesUp()
    {
        var handler = new StubHandler(_ => Redirect("https://www.example.com/favicon.ico"));

        var icon = await Fetcher(handler).FetchAsync("www.example.com", CancellationToken.None);

        icon.Should().BeNull();
        handler.Requests.Should().HaveCount(3);
    }

    [Theory]
    [InlineData("text/html")]
    [InlineData("image/svg+xml")]
    [InlineData("application/octet-stream")]
    [InlineData("application/json")]
    public async Task Fetch_ContentTypeIsNotARasterImage_IsRefused(string contentType)
    {
        var handler = new StubHandler(_ => Image(contentType, Png));

        (await Fetcher(handler).FetchAsync("www.example.com", CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task Fetch_BodyOverTheCap_IsRefused()
    {
        var handler = new StubHandler(_ => Image("image/png", new byte[MerchantIconFetcher.MaxBytes + 1]));

        (await Fetcher(handler).FetchAsync("www.example.com", CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task Fetch_BodyAtTheCap_IsAccepted()
    {
        var handler = new StubHandler(_ => Image("image/png", new byte[MerchantIconFetcher.MaxBytes]));

        (await Fetcher(handler).FetchAsync("www.example.com", CancellationToken.None)).Should().NotBeNull();
    }

    [Fact]
    public async Task Fetch_UndeclaredLengthStreamOverTheCap_IsRefused()
    {
        var handler = new StubHandler(_ =>
        {
            var content = new StreamContent(new MemoryStream(new byte[MerchantIconFetcher.MaxBytes + 10]));
            content.Headers.ContentType = new("image/png");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        });

        (await Fetcher(handler).FetchAsync("www.example.com", CancellationToken.None)).Should().BeNull();
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Fetch_ErrorStatus_ReturnsNull(HttpStatusCode status)
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(status));

        (await Fetcher(handler).FetchAsync("www.example.com", CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task Fetch_ConnectionFails_ReturnsNull()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("no route"));

        (await Fetcher(handler).FetchAsync("www.example.com", CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task Fetch_RequestTimesOut_ReturnsNull()
    {
        var handler = new StubHandler(_ => throw new TaskCanceledException("timeout"));

        (await Fetcher(handler).FetchAsync("www.example.com", CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task Fetch_CallerCancels_Propagates()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var handler = new StubHandler(_ => throw new TaskCanceledException("cancelled"));

        var act = () => Fetcher(handler).FetchAsync("www.example.com", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private static MerchantIconFetcher Fetcher(StubHandler handler) =>
        new(new HttpClient(handler), NullLogger<MerchantIconFetcher>.Instance);

    private static HttpResponseMessage Image(string contentType, byte[] body)
    {
        var content = new ByteArrayContent(body);
        content.Headers.ContentType = new(contentType);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    private static HttpResponseMessage Redirect(string location)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Moved);
        response.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
        return response;
    }

    private sealed class StubHandler(Func<Uri, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(respond(request.RequestUri!));
        }
    }
}
