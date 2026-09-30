namespace FinanceSentry.Tests.Integration.Research;

using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Moq;
using Xunit;

/// <summary>
/// The agent chat and Ledger narrative endpoints require the Owner role (<c>RequireOwner</c> policy):
/// a signed-in user without it is answered 403 before any agent call or prompt composition.
/// </summary>
public class OwnerOnlyAgentFeatureTests(AssetDossierApiFactory factory) : IClassFixture<AssetDossierApiFactory>
{
    private static readonly JsonContent ChatBody = JsonContent.Create(new { message = "hello" });

    [Fact]
    public async Task Chat_ForUserWithoutOwnerRole_Returns403_AndNeverCallsTheAgent()
    {
        factory.AgentConversationMock.Invocations.Clear();
        using var client = factory.CreateAuthenticatedClient(owner: false);

        var response = await client.PostAsync("/api/v1/agent/chat", ChatBody);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadFromJsonAsync<ErrorShape>();
        body!.ErrorCode.Should().Be("FORBIDDEN");
        factory.AgentConversationMock.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task Chat_ForOwner_PassesTheGateAndStreamsAReply()
    {
        using var client = factory.CreateAuthenticatedClient(owner: true);

        var response = await client.PostAsync("/api/v1/agent/chat", ChatBody);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/event-stream");
        (await response.Content.ReadAsStringAsync()).Should().Contain("event: error");
    }

    [Theory]
    [InlineData("GET", "/api/v1/agent/conversations")]
    [InlineData("DELETE", "/api/v1/agent/conversations/00000000-0000-0000-0000-000000000001")]
    public async Task ConversationRoutes_ForUserWithoutOwnerRole_Return403(string method, string path)
    {
        using var client = factory.CreateAuthenticatedClient(owner: false);

        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Chat_WithoutSignIn_Returns401()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/v1/agent/chat", ChatBody);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    public async Task Narrative_ForUserWithoutOwnerRole_Returns403_AndNeverCallsTheNarrator(string method)
    {
        factory.NarratorMock.Invocations.Clear();
        factory.BookFiguresMock.Invocations.Clear();
        using var client = factory.CreateAuthenticatedClient(owner: false);

        var response = await client.SendAsync(
            new HttpRequestMessage(new HttpMethod(method), "/api/v1/research/assets/AAPL/narrative"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        factory.NarratorMock.Invocations.Should().BeEmpty();
        factory.BookFiguresMock.Invocations.Should().BeEmpty("no position data is read to compose a prompt");
    }

    [Fact]
    public async Task Dossier_ForUserWithoutOwnerRole_StillWorks()
    {
        using var client = factory.CreateAuthenticatedClient(owner: false);

        var response = await client.GetAsync("/api/v1/research/assets/AAPL/dossier");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Narrative_ForOwner_InvokesTheNarrator()
    {
        factory.NarratorMock
            .Setup(n => n.NarrateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Owner read.");
        using var client = factory.CreateAuthenticatedClient(owner: true);

        var response = await client.PostAsync("/api/v1/research/assets/OWNR/narrative", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        factory.NarratorMock.Verify(
            n => n.NarrateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private sealed record ErrorShape(string Error, string ErrorCode);
}
