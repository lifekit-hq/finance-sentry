namespace FinanceSentry.Tests.Integration.Research;

using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.Auth.Domain.Entities;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

/// <summary>
/// The agent chat and Ledger narrative endpoints require the <c>ai.use</c> permission (<c>RequireAiUse</c>
/// policy), which the Owner role carries and a Member holds only when granted it per person: a signed-in user
/// without it is answered 403 before any agent call or prompt composition.
/// </summary>
public class AiUseAgentFeatureTests(AssetDossierApiFactory factory) : IClassFixture<AssetDossierApiFactory>
{
    private static readonly JsonContent ChatBody = JsonContent.Create(new { message = "hello" });
    private static readonly Claim AiUseClaim = new(Permissions.ClaimType, Permissions.AiUse);

    [Fact]
    public async Task Chat_ForMember_Returns403_AndNeverCallsTheAgent()
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
    public async Task ConversationRoutes_ForMember_Return403(string method, string path)
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
    public async Task Narrative_ForMember_Returns403_AndNeverCallsTheNarrator(string method)
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
    public async Task Dossier_ForMember_StillWorks()
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

    [Fact]
    public async Task Chat_ForMemberGrantedAiUse_PassesTheGate_AndLosesItOnTheNextRequestWhenRevoked()
    {
        var userId = Guid.NewGuid();
        using var client = factory.CreateAuthenticatedClient(userId, AuthRoles.Member);
        await WithUserAsync(userId, (users, user) => users.AddClaimAsync(user, AiUseClaim));

        var granted = await client.PostAsync("/api/v1/agent/chat", ChatBody);
        await WithUserAsync(userId, (users, user) => users.RemoveClaimAsync(user, AiUseClaim));
        var revoked = await client.PostAsync("/api/v1/agent/chat", ChatBody);

        granted.StatusCode.Should().Be(HttpStatusCode.OK);
        revoked.StatusCode.Should().Be(HttpStatusCode.Forbidden, "permissions are read per request, not from the token");
    }

    [Fact]
    public async Task Chat_ForOwnerDemotedToMember_Returns403OnTheNextRequestWithTheSameToken()
    {
        var userId = Guid.NewGuid();
        using var client = factory.CreateAuthenticatedClient(userId, AuthRoles.Owner);
        (await client.PostAsync("/api/v1/agent/chat", ChatBody)).StatusCode.Should().Be(HttpStatusCode.OK);

        await WithUserAsync(userId, async (users, user) =>
        {
            await users.RemoveFromRoleAsync(user, AuthRoles.Owner);
            return await users.AddToRoleAsync(user, AuthRoles.Member);
        });
        var response = await client.PostAsync("/api/v1/agent/chat", ChatBody);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private async Task WithUserAsync(Guid userId, Func<UserManager<ApplicationUser>, ApplicationUser, Task<IdentityResult>> change)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var result = await change(users, (await users.FindByIdAsync(userId.ToString()))!);
        result.Succeeded.Should().BeTrue();
    }

    private sealed record ErrorShape(string Error, string ErrorCode);
}
