namespace FinanceSentry.Infrastructure.Links;

using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// The one key of the <c>Companion</c> section the brief jobs of other modules read: the public origin their
/// bullets link onto. Optional — empty (or not an absolute URL, which the composers treat the same) = no links —
/// so nothing is validated. The Companion module binds the rest of the section as <c>CompanionOptions</c>.
/// </summary>
public sealed class CompanionLinksOptions
{
    public const string SectionName = "Companion";

    /// <summary>Public origin of the app; deploys bind it from <c>FRONTEND_BASE_URL</c>.</summary>
    public string? PublicBaseUrl { get; set; }
}

public static class CompanionLinksServiceCollectionExtensions
{
    public static IServiceCollection AddCompanionLinks(this IServiceCollection services)
    {
        services.AddOptions<CompanionLinksOptions>().BindConfiguration(CompanionLinksOptions.SectionName);
        return services;
    }
}
