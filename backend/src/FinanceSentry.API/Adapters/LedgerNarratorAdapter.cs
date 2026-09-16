namespace FinanceSentry.API.Adapters;

using System.Text;
using FinanceSentry.Modules.Agent.Application.Services;
using FinanceSentry.Modules.Research.Domain.Ports;
using Microsoft.Extensions.Options;

/// <summary>
/// 421/US3: bridges the Research dossier's "Ledger's read" to the 040 agent loop, so Research never
/// references the Agent module. Runs a single stateless turn — no conversation is persisted — and
/// assembles the streamed answer into one string.
/// <para>Lives in the host rather than alongside the other 039 port adapters in
/// FinanceSentry.Integration: Mcp already references Integration and Agent references Mcp, so an
/// Integration→Agent edge would be circular.</para>
/// </summary>
public sealed class LedgerNarratorAdapter(
    IAgentConversationService conversation,
    IOptions<AgentOptions> options,
    IServiceProvider requestServices) : ILedgerNarrator
{
    public async Task<string?> NarrateAsync(string prompt, CancellationToken ct = default)
    {
        if (!options.Value.IsConfigured)
        {
            return null;
        }

        var sb = new StringBuilder();
        var errored = false;

        await foreach (var evt in conversation.RunAsync([LlmMessage.UserText(prompt)], requestServices, ct))
        {
            switch (evt)
            {
                // A canned greeting stood in for a silent agent — it is not a read of the asset.
                case AgentCompletionEvent { IsSilenceFallback: true }:
                    errored = true;
                    break;
                // The terminal completion event carries the assembled answer; prefer it over deltas.
                case AgentCompletionEvent completion:
                    sb.Clear();
                    sb.Append(completion.FinalText);
                    break;
                case AgentTextEvent text:
                    sb.Append(text.Delta);
                    break;
                case AgentErrorEvent:
                    errored = true;
                    break;
                default:
                    break;
            }
        }

        return errored || sb.Length == 0 ? null : sb.ToString();
    }
}
