namespace FinanceSentry.Modules.Agent.Infrastructure;

using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FinanceSentry.Modules.Agent.Application.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// The OpenClaw-backed brain (feature 040): instead of running FS's own persona + tool loop, it
/// delegates the whole turn to the existing OpenClaw Ledger (<c>finance</c>) agent over the gateway's
/// OpenAI-compatible API (<c>POST /v1/chat/completions</c>, <c>model: openclaw/finance</c>, SSE). That
/// agent owns its persona, its MCP tool surface (finance-sentry, etc.), and its own credentials — so the
/// browser chat is the real Ledger with no Anthropic key in FS. Conversations are independent: FS is the
/// source of truth for browser threads and replays full history each turn, so the gateway is driven
/// statelessly (no OpenAI <c>user</c> session key) and browser/Telegram memories never cross.
///
/// <para>
/// Ledger's persona is tuned for proactive Telegram/cron use where "silence is the correct default": on a
/// content-free message (a greeting, an ack) it emits the OpenClaw <c>NO_REPLY</c> sentinel to stay quiet.
/// That is right for a channel but wrong for a browser chat where the user is staring at the widget waiting
/// for a reply — rendering the raw sentinel is the "NO_REPLY" bug. The <c>system</c> role is ignored by
/// this endpoint (Ledger owns its persona), so we can't disable the behaviour upstream; instead this service
/// suppresses the sentinel client-side — swapping in a greeting when the whole reply is silence, and
/// stripping a leading <c>NO_REPLY</c> if the agent self-corrects. Substantive replies stream untouched.
/// The greeting's completion is flagged <see cref="AgentCompletionEvent.IsSilenceFallback"/> so non-chat
/// callers (the Ledger read) never mistake it for an answer.
/// </para>
/// </summary>
public sealed class OpenClawAgentConversationService(
    IHttpClientFactory httpClientFactory,
    IOptions<AgentOptions> options,
    ILogger<OpenClawAgentConversationService> logger) : IAgentConversationService
{
    public const string HttpClientName = "agent-openclaw";

    /// <summary>OpenClaw's "stay silent" sentinel — never rendered in the interactive browser chat.</summary>
    private const string SilenceSentinel = "NO_REPLY";

    /// <summary>Shown when Ledger would otherwise stay silent, so the widget is never left blank.</summary>
    private const string SilenceFallback =
        "Hey Denys 👋 I'm Ledger — your finance agent. Ask me anything about your book: "
        + "positioning, a specific name, risk flags, or what's moved. What do you want to look at?";

    private const int MaxLoggedBody = 500;

    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
    private readonly AgentOptions.OpenClawOptions _openClaw = options.Value.OpenClaw;
    private readonly ILogger<OpenClawAgentConversationService> _logger = logger;

    public async IAsyncEnumerable<AgentStreamEvent> RunAsync(
        IReadOnlyList<LlmMessage> messages,
        IServiceProvider toolScope,
        [EnumeratorCancellation] CancellationToken ct)
    {
        // toolScope is unused: the OpenClaw finance agent dispatches its own tools server-side.
        var payload = BuildRequestBody(messages);
        var client = _httpClientFactory.CreateClient(HttpClientName);

        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/chat/completions")
        {
            Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        if (!string.IsNullOrWhiteSpace(_openClaw.Token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _openClaw.Token);
        }

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        HttpResponseMessage? response = null;
        try
        {
            response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OpenClaw gateway request failed.");
        }

        if (response is null)
        {
            yield return new AgentErrorEvent("llm_unavailable", "The finance agent is temporarily unavailable.");
            yield break;
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning(
                    "OpenClaw API returned {Status}: {Body}", (int)response.StatusCode, Truncate(body));
                yield return new AgentErrorEvent("llm_unavailable", "The finance agent is temporarily unavailable.");
                yield break;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream, Encoding.UTF8);

            // `visible` is what we actually emit to the client; `held` buffers the opening text while it's
            // still ambiguous whether the whole reply is just the NO_REPLY silence sentinel. Once `decided`,
            // deltas pass straight through.
            var visible = new StringBuilder();
            var held = new StringBuilder();
            var decided = false;

            while (true)
            {
                string? line;
                AgentErrorEvent? failure = null;
                try
                {
                    ct.ThrowIfCancellationRequested();
                    line = await reader.ReadLineAsync(ct);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "OpenClaw stream read failed.");
                    failure = new AgentErrorEvent("llm_unavailable", "The finance agent stream was interrupted.");
                    line = null;
                }

                if (failure is not null)
                {
                    yield return failure;
                    yield break;
                }

                if (line is null)
                {
                    break;
                }

                if (!line.StartsWith("data:", StringComparison.Ordinal))
                {
                    continue;
                }

                var data = line["data:".Length..].Trim();
                if (data.Length == 0)
                {
                    continue;
                }

                if (string.Equals(data, "[DONE]", StringComparison.Ordinal))
                {
                    break;
                }

                // A turn that fails after the 200 headers (e.g. every model failed auth) arrives as an
                // in-band `{"error":{...}}` chunk followed by [DONE] — a failure, never an empty reply.
                var chunk = ParseChunk(data);
                if (chunk is JsonObject obj && obj["error"] is { } streamError)
                {
                    _logger.LogWarning(
                        "OpenClaw stream reported an error: {Error}", Truncate(streamError.ToJsonString()));
                    yield return new AgentErrorEvent("llm_unavailable", "The finance agent is temporarily unavailable.");
                    yield break;
                }

                var delta = ExtractDelta(chunk);
                if (string.IsNullOrEmpty(delta))
                {
                    continue;
                }

                if (decided)
                {
                    visible.Append(delta);
                    yield return new AgentTextEvent(delta);
                    continue;
                }

                held.Append(delta);
                var trimmed = held.ToString().TrimStart();

                // Any prefix of "NO_REPLY" (including the full sentinel) could still be pure silence — keep
                // holding, emit nothing yet.
                if (trimmed.Length == 0 || SilenceSentinel.StartsWith(trimmed, StringComparison.Ordinal))
                {
                    continue;
                }

                // The reply diverged from the sentinel. If it opened with NO_REPLY the agent self-corrected —
                // drop the sentinel prefix and emit the rest; otherwise emit the buffered text as-is.
                decided = true;
                var emit = trimmed.StartsWith(SilenceSentinel, StringComparison.Ordinal)
                    ? trimmed[SilenceSentinel.Length..].TrimStart()
                    : trimmed;
                held.Clear();
                if (emit.Length > 0)
                {
                    visible.Append(emit);
                    yield return new AgentTextEvent(emit);
                }
            }

            // Nothing rendered — the whole reply was the silence sentinel (or empty). Never leave the widget
            // blank or show the raw sentinel: greet instead.
            if (visible.Length == 0)
            {
                yield return new AgentTextEvent(SilenceFallback);
                yield return new AgentCompletionEvent(SilenceFallback, null, IsSilenceFallback: true);
                yield break;
            }

            yield return new AgentCompletionEvent(visible.ToString(), null);
        }
    }

    private JsonObject BuildRequestBody(IReadOnlyList<LlmMessage> messages) => new()
    {
        ["model"] = _openClaw.Model,
        ["stream"] = true,
        ["messages"] = SerializeMessages(messages),
    };

    /// <summary>Flattens the FS message history into OpenAI chat messages (role + plain-text content).</summary>
    private static JsonArray SerializeMessages(IReadOnlyList<LlmMessage> messages)
    {
        var array = new JsonArray();
        foreach (var message in messages)
        {
            var text = new StringBuilder();
            foreach (var block in message.Content)
            {
                if (block is LlmTextBlock textBlock)
                {
                    text.Append(textBlock.Text);
                }
            }

            array.Add(new JsonObject { ["role"] = message.Role, ["content"] = text.ToString() });
        }

        return array;
    }

    /// <summary>Pulls the incremental assistant text from one OpenAI streaming chunk, if any.</summary>
    private static string? ExtractDelta(JsonNode? node)
    {
        var choice = node?["choices"]?.AsArray() is { Count: > 0 } choices ? choices[0] : null;
        var content = choice?["delta"]?["content"];
        return content?.GetValueKind() == JsonValueKind.String ? content.GetValue<string>() : null;
    }

    private JsonNode? ParseChunk(string data)
    {
        try
        {
            return JsonNode.Parse(data);
        }
        catch (JsonException ex)
        {
            _logger.LogDebug(ex, "Skipping unparsable OpenClaw SSE data line.");
            return null;
        }
    }

    private static string Truncate(string value) => value.Length <= MaxLoggedBody ? value : value[..MaxLoggedBody];
}
