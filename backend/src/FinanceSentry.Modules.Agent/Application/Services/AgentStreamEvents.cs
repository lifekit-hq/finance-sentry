namespace FinanceSentry.Modules.Agent.Application.Services;

/// <summary>Typed events the agent loop yields; the controller relays them to the client as SSE.</summary>
public abstract record AgentStreamEvent;

/// <summary>Sent first — the (new or existing) conversation id.</summary>
public sealed record AgentConversationEvent(Guid ConversationId) : AgentStreamEvent;

/// <summary>An assistant token delta — append in order.</summary>
public sealed record AgentTextEvent(string Delta) : AgentStreamEvent;

/// <summary>A tool call started/finished (<c>phase</c> = <c>start</c>|<c>end</c>) — progressive feedback (SC-007).</summary>
public sealed record AgentToolEvent(string Name, string Phase) : AgentStreamEvent;

/// <summary>A recoverable error; the stream then ends.</summary>
public sealed record AgentErrorEvent(string Code, string Message) : AgentStreamEvent;

/// <summary>Final assistant message persisted; stream closes.</summary>
public sealed record AgentDoneEvent(Guid MessageId) : AgentStreamEvent;

/// <summary>
/// Internal terminal event carrying the assembled final answer for persistence — consumed by the
/// command handler, never forwarded to the client.
/// </summary>
/// <param name="IsSilenceFallback">
/// True when the agent said nothing (empty reply or the OpenClaw <c>NO_REPLY</c> sentinel) and
/// <paramref name="FinalText"/> is a canned greeting substituted for the chat widget. That text is
/// never an answer to the prompt, so non-chat consumers must treat it as "no usable text".
/// </param>
public sealed record AgentCompletionEvent(string FinalText, string? ToolCallsJson, bool IsSilenceFallback = false)
    : AgentStreamEvent;
