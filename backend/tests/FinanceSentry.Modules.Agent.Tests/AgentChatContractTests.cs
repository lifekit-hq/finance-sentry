namespace FinanceSentry.Modules.Agent.Tests;

using System.Security.Claims;
using System.Text;
using FinanceSentry.API.Controllers;
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
    private static AgentDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AgentDbContext>()
            .UseInMemoryDatabase($"agent-{Guid.NewGuid()}")
            .Options);

    private static AgentOptions ConfiguredOptions()
    {
        var options = new AgentOptions { PersonaRootPath = RepoPaths.RepoRoot() };
        options.Anthropic.ApiKey = "sk-ant-test";
        return options;
    }

    private static Harness BuildHarness(AgentOptions options, ILlmClient llm)
    {
        var db = NewDb();
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

        await harness.Controller.Chat(new AgentChatRequest(null, "what's my net worth?"), CancellationToken.None);

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
    public async Task Chat_Keyless_EmitsSingleAgentNotConfiguredError()
    {
        var options = new AgentOptions { PersonaRootPath = RepoPaths.RepoRoot() }; // no key
        var harness = BuildHarness(options, new FakeLlmClient().EnqueueText("should not run"));
        Authenticate(harness.Controller, Guid.NewGuid(), out var body);

        await harness.Controller.Chat(new AgentChatRequest(null, "hi"), CancellationToken.None);

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
        await harness.Controller.Chat(new AgentChatRequest(foreign.Id, "peek"), CancellationToken.None);

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

    private sealed record Harness(AgentChatController Controller, ConversationRepository Repo, AgentDbContext Db);
}
