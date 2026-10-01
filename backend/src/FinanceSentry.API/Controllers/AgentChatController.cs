namespace FinanceSentry.API.Controllers;

using System.Text.Json;
using FinanceSentry.Core.Api;
using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Agent.Application.Commands;
using FinanceSentry.Modules.Agent.Application.Queries;
using FinanceSentry.Modules.Agent.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;

/// <summary>
/// The in-app finance agent (Ledger) surface — feature 040 US2. Requires the <c>ai.use</c> permission (<see cref="AuthPolicies.RequireAiUse"/>); every
/// route is scoped to the caller's user, and no user id in the path/body is honored. Chat streams over
/// Server-Sent Events per <c>contracts/chat-endpoint.md</c>. Keyless ⇒ a single <c>agent_not_configured</c>
/// error event, no model call.
/// </summary>
[ApiController]
[Authorize(Policy = AuthPolicies.RequireAiUse)]
[Route("agent")]
public sealed class AgentChatController(
    ICommandHandler<SendAgentMessageCommand, IAsyncEnumerable<AgentStreamEvent>> sendHandler,
    IQueryHandler<ListConversationsQuery, IReadOnlyList<ConversationSummaryDto>> listHandler,
    IQueryHandler<GetConversationQuery, ConversationDetailDto?> getHandler,
    ICommandHandler<DeleteConversationCommand, bool> deleteHandler) : ControllerBase
{
    private static readonly JsonSerializerOptions SseJson = new(JsonSerializerDefaults.Web);

    private readonly ICommandHandler<SendAgentMessageCommand, IAsyncEnumerable<AgentStreamEvent>> _sendHandler = sendHandler;
    private readonly IQueryHandler<ListConversationsQuery, IReadOnlyList<ConversationSummaryDto>> _listHandler = listHandler;
    private readonly IQueryHandler<GetConversationQuery, ConversationDetailDto?> _getHandler = getHandler;
    private readonly ICommandHandler<DeleteConversationCommand, bool> _deleteHandler = deleteHandler;

    /// <summary>Send a message and stream the reply (SSE).</summary>
    [HttpPost("chat")]
    public async Task Chat([FromBody] AgentChatRequest request, CancellationToken ct)
    {
        var userId = User.RequireUserId();

        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";
        HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

        var message = request.Message ?? string.Empty;
        var command = new SendAgentMessageCommand(userId, request.ConversationId, message);
        var stream = await _sendHandler.Handle(command, ct);

        await foreach (var evt in stream.WithCancellation(ct))
        {
            var (name, payload) = Map(evt);
            if (name is null)
            {
                continue;
            }

            await WriteEventAsync(name, payload!, ct);
        }
    }

    /// <summary>List the caller's conversations, newest first.</summary>
    [HttpGet("conversations")]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var conversations = await _listHandler.Handle(new ListConversationsQuery(User.RequireUserId()), ct);
        return Ok(conversations);
    }

    /// <summary>Full history for one conversation. 404 when not owned by the caller.</summary>
    [HttpGet("conversations/{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var conversation = await _getHandler.Handle(new GetConversationQuery(User.RequireUserId(), id), ct);
        return conversation is null
            ? NotFound(new ApiErrorBody("Conversation not found.", "conversation_not_found"))
            : Ok(conversation);
    }

    /// <summary>Delete a conversation (cascade messages). Requires <c>ai.use</c>.</summary>
    [HttpDelete("conversations/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var deleted = await _deleteHandler.Handle(new DeleteConversationCommand(User.RequireUserId(), id), ct);
        return deleted
            ? NoContent()
            : NotFound(new ApiErrorBody("Conversation not found.", "conversation_not_found"));
    }

    private static (string? Name, object? Payload) Map(AgentStreamEvent evt) => evt switch
    {
        AgentConversationEvent c => ("conversation", new { conversationId = c.ConversationId }),
        AgentTextEvent t => ("text", new { delta = t.Delta }),
        AgentToolEvent tool => ("tool", new { name = tool.Name, phase = tool.Phase }),
        AgentErrorEvent e => ("error", new { code = e.Code, message = e.Message }),
        AgentDoneEvent d => ("done", new { messageId = d.MessageId }),
        _ => (null, null),
    };

    private async Task WriteEventAsync(string name, object payload, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(payload, SseJson);
        await Response.WriteAsync($"event: {name}\ndata: {json}\n\n", ct);
        await Response.Body.FlushAsync(ct);
    }
}

/// <summary>Chat request body. <c>conversationId</c> null ⇒ a new conversation is created.</summary>
public sealed record AgentChatRequest(Guid? ConversationId, string? Message);
