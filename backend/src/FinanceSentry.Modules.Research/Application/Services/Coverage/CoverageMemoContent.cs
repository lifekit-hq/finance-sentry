namespace FinanceSentry.Modules.Research.Application.Services.Coverage;

using System.Text.Json;
using System.Text.Json.Serialization;
using FinanceSentry.Modules.Research.Domain.ThesisMonitor;

/// <summary>
/// One thing that would make the memo's call wrong. A <c>metric</c> falsifier names a metric of the thesis-trigger
/// vocabulary (<see cref="ThesisMetric"/>) with a direction and threshold the thesis monitor can later evaluate; a
/// <c>narrative</c> one is a sentence only.
/// </summary>
public sealed record CoverageMemoFalsifier(
    string Kind, string Statement, string? Metric = null, string? Direction = null, decimal? Threshold = null);

/// <summary>The memo shape the model is asked for (see <see cref="CoverageMemoPrompt"/>).</summary>
public sealed record CoverageMemoContent(
    string Business,
    string WhyNow,
    IReadOnlyList<CoverageMemoFalsifier> Falsifiers,
    string Crowding,
    string CorrelationToBook,
    string Verdict,
    string Caveats);

/// <summary>Reads a model reply into <see cref="CoverageMemoContent"/>; pure.</summary>
public static class CoverageMemoParser
{
    public const string Buy = "Buy";
    public const string Watch = "Watch";
    public const string Avoid = "Avoid";

    public const string MetricKind = "metric";
    public const string NarrativeKind = "narrative";

    private static readonly string[] Verdicts = [Buy, Watch, Avoid];
    private static readonly string[] Directions = ["above", "below"];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// The memo in <paramref name="reply"/>, or null when it is not the memo shape (not JSON, a missing field, a verdict
    /// outside <see cref="Verdicts"/>, no falsifier). A metric falsifier that names a metric outside the thesis
    /// vocabulary, or lacks a direction or threshold, is kept as a narrative one, never dropped.
    /// </summary>
    public static CoverageMemoContent? TryParse(string reply)
    {
        var start = reply.IndexOf('{', StringComparison.Ordinal);
        var end = reply.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return null;
        }

        Raw? raw;
        try
        {
            raw = JsonSerializer.Deserialize<Raw>(reply[start..(end + 1)], Json);
        }
        catch (JsonException)
        {
            return null;
        }

        var verdict = Verdicts.FirstOrDefault(v => string.Equals(v, raw?.Verdict?.Trim(), StringComparison.OrdinalIgnoreCase));
        var falsifiers = (raw?.Falsifiers ?? []).Select(Normalise).OfType<CoverageMemoFalsifier>().ToList();
        if (raw is null || verdict is null || falsifiers.Count == 0
            || string.IsNullOrWhiteSpace(raw.Business) || string.IsNullOrWhiteSpace(raw.WhyNow))
        {
            return null;
        }

        return new CoverageMemoContent(
            raw.Business.Trim(),
            raw.WhyNow.Trim(),
            falsifiers,
            raw.Crowding?.Trim() ?? string.Empty,
            raw.CorrelationToBook?.Trim() ?? string.Empty,
            verdict,
            raw.Caveats?.Trim() ?? string.Empty);
    }

    public static string Serialize(CoverageMemoContent content) => JsonSerializer.Serialize(content, Json);

    public static CoverageMemoContent? Deserialize(string body)
    {
        try
        {
            return JsonSerializer.Deserialize<CoverageMemoContent>(body, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static CoverageMemoFalsifier? Normalise(RawFalsifier? falsifier)
    {
        if (falsifier is null || string.IsNullOrWhiteSpace(falsifier.Statement))
        {
            return null;
        }

        var direction = Directions.FirstOrDefault(d => string.Equals(d, falsifier.Direction?.Trim(), StringComparison.OrdinalIgnoreCase));
        var metric = falsifier.Metric?.Trim();
        var evaluable = string.Equals(falsifier.Kind?.Trim(), MetricKind, StringComparison.OrdinalIgnoreCase)
            && metric is not null && ThesisMetric.Contains(metric) && direction is not null && falsifier.Threshold is not null;
        return evaluable
            ? new CoverageMemoFalsifier(MetricKind, falsifier.Statement.Trim(), metric, direction, falsifier.Threshold)
            : new CoverageMemoFalsifier(NarrativeKind, falsifier.Statement.Trim());
    }

    private sealed record Raw(
        string? Business, string? WhyNow, List<RawFalsifier?>? Falsifiers, string? Crowding, string? CorrelationToBook,
        string? Verdict, string? Caveats);

    private sealed record RawFalsifier(string? Kind, string? Statement, string? Metric, string? Direction, decimal? Threshold);
}
