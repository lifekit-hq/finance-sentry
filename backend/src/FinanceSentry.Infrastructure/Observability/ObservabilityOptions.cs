namespace FinanceSentry.Infrastructure.Observability;

using System.ComponentModel.DataAnnotations;
using FinanceSentry.Core.Configuration;
using Microsoft.Extensions.Configuration;

/// <summary>
/// The <c>Observability</c> configuration section. Logging and tracing are configured before the host's
/// container exists, so this is bound straight from <see cref="IConfiguration"/> via <see cref="From"/>
/// instead of <c>AddOptions</c>. Everything is optional: an absent OTLP endpoint falls back to the
/// in-network collector; whatever is set must be a well-formed URL.
/// </summary>
public sealed class ObservabilityOptions
{
    public const string SectionName = "Observability";

    public const string DefaultOtlpEndpoint = "http://otel-collector:4318";

    public OtlpOptions Otlp { get; set; } = new();

    /// <summary>Binds and validates the section; throws <see cref="ValidationException"/> on a malformed URL.</summary>
    public static ObservabilityOptions From(IConfiguration configuration)
    {
        var options = configuration.GetSection(SectionName).Get<ObservabilityOptions>() ?? new ObservabilityOptions();
        Validator.ValidateObject(options.Otlp, new ValidationContext(options.Otlp), validateAllProperties: true);
        return options;
    }

    public sealed class OtlpOptions
    {
        /// <summary>Unset = the in-network collector default; an explicit empty value disables the exporter (dev).</summary>
        [OptionalUrl]
        public string? Endpoint { get; set; } = DefaultOtlpEndpoint;
    }
}
