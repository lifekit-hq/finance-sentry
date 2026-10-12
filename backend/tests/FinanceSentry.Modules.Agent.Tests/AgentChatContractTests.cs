namespace FinanceSentry.Modules.Agent.Tests;

using System.Net.ServerSentEvents;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Nodes;
using FinanceSentry.API.Controllers;
using FinanceSentry.Infrastructure.Auth;
using FinanceSentry.Modules.Agent.Application.Commands;
using FinanceSentry.Modules.Agent.Application.Queries;
using FinanceSentry.Modules.Agent.Application.Services;
using FinanceSentry.Modules.Agent.Infrastructure;
using FinanceSentry.Modules.Agent.Infrastructure.Repositories;
using FinanceSentry.Modules.Agent.Tests.Support;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

/// <summary>
/// Contract test for the chat endpoint (SSE event shape, keyless path, cross-user 404, auth requirement).
/// The controller is exercised directly with an in-memory DbContext + a scripted LLM — no real API call.
/// </summary>
public sealed class AgentChatContractTests
{
    // The context reads the controller's principal through the API's own ICurrentUser, so the Owner query
    // filter scopes each call to whoever Authenticate signed in.
    private static AgentDbContext NewDb(IHttpContextAccessor httpContext) =>
        new(new DbContextOptionsBuilder<AgentDbContext>()
            .UseInMemoryDatabase($"agent-{Guid.NewGuid()}")
            .Options,
            new HttpContextCurrentUser(httpContext));

    private static AgentOptions ConfiguredOptions()
    {
        var options = new AgentOptions { PersonaRootPath = RepoPaths.RepoRoot() };
        options.Anthropic.ApiKey = "sk-ant-test";
        return options;
    }

    private static Harness BuildHarness(AgentOptions options, ILlmClient llm)
    {
        var httpContext = new ControllerHttpContextAccessor();
        var db = NewDb(httpContext);
        var repo = new ConversationRepository(db);
        var opts = Options.Create(options);

        var bridge = new McpToolBridge(opts, NullLogger<McpToolBridge>.Instance, typeof(FakeEchoTool).Assembly);
        var persona = new PersonaComposer(opts, NullLogger<PersonaComposer>.Instance);
        var service = new AgentConversationService(llm, bridge, persona, opts, NullLogger<AgentConversationService>.Instance);

        var toolScope = new ServiceCollection()
            .AddScoped<IFakeIdentity>(_ => new FakeIdentity(Guid.NewGuid()))
            .AddScoped<FakeEchoTool>()
            .BuildServiceProvider();

        var sendHandler = new SendAgentMessageCommandHandler(repo, service, opts, toolScope);
        var listHandler = new ListConversationsQueryHandler(repo);
        var getHandler = new GetConversationQueryHandler(repo);
        var deleteHandler = new DeleteConversationCommandHandler(repo);

        var controller = new AgentChatController(sendHandler, listHandler, getHandler, deleteHandler);
        httpContext.Controller = controller;
        return new Harness(controller, repo, db);
    }

    private static void Authenticate(ControllerBase controller, Guid userId, out MemoryStream body)
    {
        body = new MemoryStream();
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Test");
        var httpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
        httpContext.Response.Body = body;
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
    }

    [Fact]
    public async Task Chat_StreamsConversation_Text_AndDone()
    {
        var userId = Guid.NewGuid();
        var harness = BuildHarness(ConfiguredOptions(), new FakeLlmClient().EnqueueText("Net worth: $1.8M."));
        Authenticate(harness.Controller, userId, out var body);

        await ExecuteChatAsync(harness.Controller, new AgentChatRequest(null, "what's my net worth?"));

        var sse = Encoding.UTF8.GetString(body.ToArray());
        sse.Should().Contain("event: conversation");
        sse.Should().Contain("event: text");
        sse.Should().Contain("Net worth: $1.8M.");
        sse.Should().Contain("event: done");

        var conversations = await harness.Repo.ListAsync(userId, CancellationToken.None);
        conversations.Should().ContainSingle();
        var detail = await harness.Repo.GetWithMessagesAsync(userId, conversations[0].Id, CancellationToken.None);
        detail!.Messages.Should().HaveCount(2); // user + assistant
    }

    [Fact]
    public async Task Chat_EventStream_RoundTripsThroughSseParser_IncludingNewlinePayload()
    {
        const string reply = "line one\nline two\n\nevent: injected";
        var harness = BuildHarness(ConfiguredOptions(), new FakeLlmClient().EnqueueText(reply));
        Authenticate(harness.Controller, Guid.NewGuid(), out var body);

        await ExecuteChatAsync(harness.Controller, new AgentChatRequest(null, "hi"));

        body.Position = 0;
        var items = new List<SseItem<string>>();
        await foreach (var item in SseParser.Create(body).EnumerateAsync())
        {
            items.Add(item);
        }

        items.Select(i => i.EventType).Should().Equal("conversation", "text", "done");
        items.All(i => !i.Data.Contains('\n')).Should().BeTrue();
        JsonNode.Parse(items[0].Data)!["conversationId"]!.GetValue<Guid>().Should().NotBeEmpty();
        JsonNode.Parse(items[1].Data)!["delta"]!.GetValue<string>().Should().Be(reply);
        JsonNode.Parse(items[2].Data)!["messageId"].Should().NotBeNull();
    }

    [Fact]
    public async Task Chat_SetsSseHeaders()
    {
        var harness = BuildHarness(ConfiguredOptions(), new FakeLlmClient().EnqueueText("hi"));
        Authenticate(harness.Controller, Guid.NewGuid(), out _);

        await ExecuteChatAsync(harness.Controller, new AgentChatRequest(null, "hi"));

        var response = harness.Controller.ControllerContext.HttpContext.Response;
        response.ContentType.Should().StartWith("text/event-stream");
        response.Headers.CacheControl.ToString().Should().Contain("no-cache");
        response.Headers["X-Accel-Buffering"].ToString().Should().Be("no");
    }

    [Fact]
    public async Task Chat_Keyless_EmitsSingleAgentNotConfiguredError()
    {
        var options = new AgentOptions { PersonaRootPath = RepoPaths.RepoRoot() }; // no key
        var harness = BuildHarness(options, new FakeLlmClient().EnqueueText("should not run"));
        Authenticate(harness.Controller, Guid.NewGuid(), out var body);

        await ExecuteChatAsync(harness.Controller, new AgentChatRequest(null, "hi"));

        var sse = Encoding.UTF8.GetString(body.ToArray());
        sse.Should().Contain("event: error");
        sse.Should().Contain("agent_not_configured");
        sse.Should().NotContain("event: done");
    }

    [Fact]
    public async Task Chat_ForeignConversationId_EmitsConversationNotFound()
    {
        var harness = BuildHarness(ConfiguredOptions(), new FakeLlmClient().EnqueueText("hi"));

        // A conversation owned by another user.
        var otherUser = Guid.NewGuid();
        var foreign = await harness.Repo.CreateAsync(otherUser, "theirs", "claude-sonnet-5", CancellationToken.None);

        Authenticate(harness.Controller, Guid.NewGuid(), out var body);
        await ExecuteChatAsync(harness.Controller, new AgentChatRequest(foreign.Id, "peek"));

        var sse = Encoding.UTF8.GetString(body.ToArray());
        sse.Should().Contain("conversation_not_found");
    }

    [Fact]
    public async Task Get_ForeignConversation_Returns404()
    {
        var harness = BuildHarness(ConfiguredOptions(), new FakeLlmClient());
        var otherUser = Guid.NewGuid();
        var foreign = await harness.Repo.CreateAsync(otherUser, "theirs", "claude-sonnet-5", CancellationToken.None);

        Authenticate(harness.Controller, Guid.NewGuid(), out _);
        var result = await harness.Controller.Get(foreign.Id, CancellationToken.None);

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task Delete_OwnConversation_Then404OnForeign()
    {
        var owner = Guid.NewGuid();
        var harness = BuildHarness(ConfiguredOptions(), new FakeLlmClient());
        var mine = await harness.Repo.CreateAsync(owner, "mine", "claude-sonnet-5", CancellationToken.None);
        var foreign = await harness.Repo.CreateAsync(Guid.NewGuid(), "theirs", "claude-sonnet-5", CancellationToken.None);

        Authenticate(harness.Controller, owner, out _);
        (await harness.Controller.Delete(mine.Id, CancellationToken.None)).Should().BeOfType<NoContentResult>();
        (await harness.Controller.Delete(foreign.Id, CancellationToken.None)).Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public void Chat_Unauthenticated_IsRejectedBeforeStreaming()
    {
        // The API's fallback authorization policy issues the 401; at the controller boundary an unauthenticated
        // principal has no user id, so RequireUserId throws rather than leaking a stream.
        var harness = BuildHarness(ConfiguredOptions(), new FakeLlmClient());
        var body = new MemoryStream();
        var httpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) };
        httpContext.Response.Body = body;
        harness.Controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var act = async () => await harness.Controller.Chat(new AgentChatRequest(null, "hi"), CancellationToken.None);
        act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    private static async Task ExecuteChatAsync(AgentChatController controller, AgentChatRequest request)
    {
        var httpContext = controller.ControllerContext.HttpContext;
        httpContext.RequestServices = new ServiceCollection().AddLogging().AddOptions().BuildServiceProvider();
        var result = await controller.Chat(request, CancellationToken.None);
        await result.ExecuteAsync(httpContext);
    }

    private sealed record Harness(AgentChatController Controller, ConversationRepository Repo, AgentDbContext Db);

    private sealed class ControllerHttpContextAccessor : IHttpContextAccessor
    {
        public ControllerBase? Controller { get; set; }

        public HttpContext? HttpContext
        {
            get => Controller?.ControllerContext.HttpContext;
            set => throw new NotSupportedException();
        }
    }
}
