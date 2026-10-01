namespace FinanceSentry.Modules.Radar.Application.Services;

using System.Text.Json;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Radar.Domain;
using FinanceSentry.Modules.Radar.Domain.Repositories;
using Microsoft.Extensions.Options;

/// <summary>
/// <see cref="IRadarSignalWriter"/> impl: appends to the shared log. <c>notable</c>+ signals are
/// deduped by DedupKey within the configured silence window; <c>info</c> is recorded every run.
/// The log is append-only — no scanner ever updates another's rows.
/// </summary>
public sealed class RadarSignalWriter(
    IRadarSignalRepository signals,
    IOptions<RadarOptions> options) : IRadarSignalWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly RadarOptions _options = options.Value;

    public async Task<bool> AppendSignalAsync(RadarSignalRequest request, CancellationToken ct = default)
    {
        if (request.OneTime || request.Severity is not SignalSeverity.Info)
        {
            // OneTime: ever-seen suppression, regardless of severity.
            var since = request.OneTime
                ? DateTimeOffset.MinValue
                : DateTimeOffset.UtcNow - TimeSpan.FromHours(_options.SilenceWindowHours);
            if (await signals.HasRecentUnscopedAsync(request.DedupKey, since, ct))
            {
                return false;
            }
        }

        await signals.AppendAsync(new RadarSignal
        {
            Timestamp = DateTimeOffset.UtcNow,
            Scanner = request.Scanner,
            SignalType = request.SignalType,
            Severity = request.Severity,
            SubjectType = request.SubjectType,
            Subject = request.Subject,
            UserId = request.UserId,
            DedupKey = request.DedupKey,
            Payload = ToPayload(request.Payload),
            PayloadVersion = request.PayloadVersion,
        }, ct);
        return true;
    }

    /// <summary>
    /// Round-trips any payload object (anonymous type, record, dictionary) through Web JSON into a
    /// string-keyed dictionary whose values are <see cref="JsonElement"/> — the shape the jsonb
    /// column stores and reads back deterministically.
    /// </summary>
    private static Dictionary<string, object> ToPayload(object payload)
    {
        if (payload is Dictionary<string, object> ready)
        {
            return ready;
        }

        var json = JsonSerializer.Serialize(payload, JsonOptions);
        return JsonSerializer.Deserialize<Dictionary<string, object>>(json, JsonOptions) ?? new();
    }
}
