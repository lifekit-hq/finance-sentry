using System.ComponentModel.DataAnnotations;

namespace FinanceSentry.Gateway;

/// <summary>
/// The <c>Observability:Otlp</c> keys the gateway reads (same keys as the api's <c>ObservabilityOptions</c>; the
/// gateway references no other project). Bound before the container exists, so it comes straight from
/// configuration via <see cref="From"/>. Unset = the in-network collector, an explicit empty value disables the
/// exporter (dev); a set value must be a well-formed URL.
/// </summary>
public sealed class GatewayObservabilityOptions
{
    public const string SectionName = "Observability";

    public const string DefaultOtlpEndpoint = "http://otel-collector:4318";

    public OtlpOptions Otlp { get; set; } = new();

    public static GatewayObservabilityOptions From(IConfiguration configuration)
    {
        var options = configuration.GetSection(SectionName).Get<GatewayObservabilityOptions>() ?? new GatewayObservabilityOptions();
        Validator.ValidateObject(options.Otlp, new ValidationContext(options.Otlp), validateAllProperties: true);
        return options;
    }

    public sealed class OtlpOptions
    {
        [OptionalUrl]
        public string? Endpoint { get; set; } = DefaultOtlpEndpoint;
    }
}

/// <summary><c>[Url]</c> that lets an unset or empty value through (the empty value disables the exporter).</summary>
[AttributeUsage(AttributeTargets.Property)]
internal sealed class OptionalUrlAttribute : ValidationAttribute
{
    private static readonly UrlAttribute Url = new();

    public override bool IsValid(object? value) => value is null or "" || Url.IsValid(value);
}
