namespace FinanceSentry.Modules.Agent.Infrastructure;

using System.Text;
using System.Text.Json.Nodes;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Agent.Application.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// <see cref="IMemoLanguageModel"/> over the Anthropic Messages API for the discovery funnel's one model call: a
/// single non-streaming turn with no tools, on the same credentials and named HTTP client as the in-app agent. The
/// reply carries the token usage the caller prices the call by. Keyless means not configured: no HTTP call is made.
/// </summary>
public sealed class AnthropicMemoLanguageModel(
    IHttpClientFactory httpClientFactory,
    IOptions<AgentOptions> options,
    ILogger<AnthropicMemoLanguageModel> logger) : IMemoLanguageModel
{
    private const int LoggedBodyChars = 500;

    private readonly AgentOptions _options = options.Value;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.Anthropic.ApiKey);

    public async Task<MemoModelReply> CompleteAsync(MemoModelRequest request, CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            throw new AgentNotConfiguredException();
        }

        var model = string.IsNullOrWhiteSpace(request.ModelId) ? _options.ModelId : request.ModelId;
        var body = new JsonObject
        {
            ["model"] = model,
            ["max_tokens"] = request.MaxOutputTokens,
            ["system"] = request.System,
            ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = request.User }),
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, "/v1/messages")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        message.Headers.TryAddWithoutValidation("x-api-key", _options.Anthropic.ApiKey);
        message.Headers.TryAddWithoutValidation("anthropic-version", _options.Anthropic.ApiVersion);

        var client = httpClientFactory.CreateClient(AnthropicLlmClient.HttpClientName);
        using var response = await client.SendAsync(message, ct);
        var raw = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "Anthropic Messages API returned {Status} for a memo call: {Body}",
                (int)response.StatusCode, raw.Length <= LoggedBodyChars ? raw : raw[..LoggedBodyChars]);
            throw new HttpRequestException($"Anthropic API error {(int)response.StatusCode}.");
        }

        var node = JsonNode.Parse(raw) ?? throw new HttpRequestException("Anthropic API returned an empty body.");
        var text = new StringBuilder();
        foreach (var block in node["content"]?.AsArray() ?? [])
        {
            if (block?["type"]?.GetValue<string>() == "text")
            {
                text.Append(block["text"]?.GetValue<string>());
            }
        }

        return new MemoModelReply(
            text.ToString(),
            node["model"]?.GetValue<string>() ?? model,
            node["usage"]?["input_tokens"]?.GetValue<int>() ?? 0,
            node["usage"]?["output_tokens"]?.GetValue<int>() ?? 0);
    }
}
