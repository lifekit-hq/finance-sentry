namespace FinanceSentry.Modules.Agent.Infrastructure;

using System.Net.Http.Headers;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FinanceSentry.Modules.Agent.Application.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Thin typed client over the Anthropic Messages API (streaming + tool-use), built on
/// <see cref="IHttpClientFactory"/> + <see cref="System.Text.Json"/> — no heavy SDK, consistent with
/// FS's plain-REST integrations. Keyless ⇒ <see cref="AgentNotConfiguredException"/>, no HTTP call.
/// </summary>
public sealed class AnthropicLlmClient(
    IHttpClientFactory httpClientFactory,
    IOptions<AgentOptions> options,
    ILogger<AnthropicLlmClient> logger) : ILlmClient
{
    public const string HttpClientName = "agent-anthropic";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
    private readonly AgentOptions _options = options.Value;
    private readonly ILogger<AnthropicLlmClient> _logger = logger;

    public async IAsyncEnumerable<LlmStreamChunk> StreamAsync(
        string system,
        IReadOnlyList<LlmMessage> messages,
        IReadOnlyList<LlmTool> tools,
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (!_options.IsConfigured)
        {
            throw new AgentNotConfiguredException();
        }

        var payload = BuildRequestBody(system, messages, tools);
        var client = _httpClientFactory.CreateClient(HttpClientName);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/messages")
        {
            Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("x-api-key", _options.Anthropic.ApiKey);
        request.Headers.TryAddWithoutValidation("anthropic-version", _options.Anthropic.ApiVersion);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning("Anthropic Messages API returned {Status}: {Body}", (int)response.StatusCode, Truncate(body));
            throw new HttpRequestException($"Anthropic API error {(int)response.StatusCode}.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);

        var accumulator = new StreamAccumulator();

        await foreach (var sse in SseParser.Create(stream).EnumerateAsync(ct))
        {
            var data = sse.Data.Trim();
            if (data.Length == 0)
            {
                continue;
            }

            JsonNode? node;
            try
            {
                node = JsonNode.Parse(data);
            }
            catch (JsonException ex)
            {
                _logger.LogDebug(ex, "Skipping unparsable SSE data line.");
                continue;
            }

            var type = node?["type"]?.GetValue<string>();
            switch (type)
            {
                case "content_block_start":
                    accumulator.OnBlockStart(node!);
                    break;
                case "content_block_delta":
                    var text = accumulator.OnBlockDelta(node!);
                    if (text is not null)
                    {
                        yield return new LlmTextDelta(text);
                    }
                    break;
                case "content_block_stop":
                    accumulator.OnBlockStop(node!);
                    break;
                case "message_delta":
                    accumulator.OnMessageDelta(node!);
                    break;
                case "message_stop":
                    yield return accumulator.Complete();
                    break;
                case "error":
                    var message = node?["error"]?["message"]?.GetValue<string>() ?? "Anthropic stream error.";
                    _logger.LogWarning("Anthropic stream error: {Message}", message);
                    throw new HttpRequestException(message);
                default:
                    break;
            }
        }
    }

    private JsonObject BuildRequestBody(string system, IReadOnlyList<LlmMessage> messages, IReadOnlyList<LlmTool> tools)
    {
        var body = new JsonObject
        {
            ["model"] = _options.ModelId,
            ["max_tokens"] = _options.MaxTokens,
            ["stream"] = true,
            ["system"] = system,
            ["messages"] = SerializeMessages(messages),
        };

        if (tools.Count > 0)
        {
            var toolArray = new JsonArray();
            foreach (var tool in tools)
            {
                toolArray.Add(new JsonObject
                {
                    ["name"] = tool.Name,
                    ["description"] = tool.Description,
                    ["input_schema"] = tool.InputSchema.DeepClone(),
                });
            }

            body["tools"] = toolArray;
        }

        return body;
    }

    private static JsonArray SerializeMessages(IReadOnlyList<LlmMessage> messages)
    {
        var array = new JsonArray();
        foreach (var message in messages)
        {
            var content = new JsonArray();
            foreach (var block in message.Content)
            {
                content.Add(SerializeBlock(block));
            }

            array.Add(new JsonObject { ["role"] = message.Role, ["content"] = content });
        }

        return array;
    }

    private static JsonObject SerializeBlock(LlmContentBlock block) => block switch
    {
        LlmTextBlock text => new JsonObject { ["type"] = "text", ["text"] = text.Text },
        LlmToolUseBlock toolUse => new JsonObject
        {
            ["type"] = "tool_use",
            ["id"] = toolUse.Id,
            ["name"] = toolUse.Name,
            ["input"] = JsonNode.Parse(toolUse.Input.GetRawText()) ?? new JsonObject(),
        },
        LlmToolResultBlock toolResult => new JsonObject
        {
            ["type"] = "tool_result",
            ["tool_use_id"] = toolResult.ToolUseId,
            ["content"] = toolResult.Content,
            ["is_error"] = toolResult.IsError,
        },
        _ => throw new InvalidOperationException($"Unknown content block {block.GetType().Name}."),
    };

    private static string Truncate(string value) => value.Length <= 500 ? value : value[..500];

    /// <summary>Reassembles streamed content blocks (indexed) into a complete assistant message.</summary>
    private sealed class StreamAccumulator
    {
        private readonly Dictionary<int, BlockBuilder> _blocks = [];
        private string? _stopReason;

        public void OnBlockStart(JsonNode node)
        {
            var index = node["index"]!.GetValue<int>();
            var block = node["content_block"]!;
            var type = block["type"]!.GetValue<string>();
            _blocks[index] = new BlockBuilder
            {
                Type = type,
                Id = block["id"]?.GetValue<string>(),
                Name = block["name"]?.GetValue<string>(),
            };
        }

        public string? OnBlockDelta(JsonNode node)
        {
            var index = node["index"]!.GetValue<int>();
            if (!_blocks.TryGetValue(index, out var builder))
            {
                return null;
            }

            var delta = node["delta"]!;
            var deltaType = delta["type"]?.GetValue<string>();
            if (deltaType == "text_delta")
            {
                var text = delta["text"]?.GetValue<string>() ?? string.Empty;
                builder.Text.Append(text);
                return text;
            }

            if (deltaType == "input_json_delta")
            {
                builder.InputJson.Append(delta["partial_json"]?.GetValue<string>() ?? string.Empty);
            }

            return null;
        }

        public void OnBlockStop(JsonNode node)
        {
            // Full block content is already accumulated; parsing happens at Complete().
        }

        public void OnMessageDelta(JsonNode node)
        {
            _stopReason = node["delta"]?["stop_reason"]?.GetValue<string>() ?? _stopReason;
        }

        public LlmMessageCompleted Complete()
        {
            var blocks = new List<LlmContentBlock>();
            foreach (var (_, builder) in _blocks.OrderBy(kvp => kvp.Key))
            {
                if (builder.Type == "text")
                {
                    blocks.Add(new LlmTextBlock(builder.Text.ToString()));
                }
                else if (builder.Type == "tool_use")
                {
                    var raw = builder.InputJson.Length > 0 ? builder.InputJson.ToString() : "{}";
                    JsonElement input;
                    try
                    {
                        input = JsonDocument.Parse(raw).RootElement.Clone();
                    }
                    catch (JsonException)
                    {
                        input = JsonDocument.Parse("{}").RootElement.Clone();
                    }

                    blocks.Add(new LlmToolUseBlock(builder.Id ?? Guid.NewGuid().ToString(), builder.Name ?? string.Empty, input));
                }
            }

            return new LlmMessageCompleted(blocks, _stopReason);
        }

        private sealed class BlockBuilder
        {
            public string Type { get; init; } = "text";

            public string? Id { get; init; }

            public string? Name { get; init; }

            public StringBuilder Text { get; } = new();

            public StringBuilder InputJson { get; } = new();
        }
    }
}
