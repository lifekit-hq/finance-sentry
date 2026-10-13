using System.Text.Json.Serialization;

namespace FinanceSentry.Modules.Auth.Application.Commands;

/// <summary>
/// A person's appearance as the app shell stores it: the choices only (<see cref="Seed"/> and
/// <see cref="Intensity"/> are inputs to the tokens engine, never the derived CSS). Absent seed or
/// intensity means the app's own default seed and the engine's default intensity.
/// </summary>
public record AppearanceDto(
    string Theme,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Seed,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? Intensity);

/// <summary>
/// Body of <c>GET</c> and <c>PATCH /me/preferences</c>, the account tier of the lifekit app-shell standard.
/// </summary>
public record PreferencesDto(AppearanceDto Appearance);

/// <summary>
/// Body of <c>PATCH /me/preferences</c>: a present top-level field replaces the stored one wholesale (an
/// <c>appearance</c> object is written as a whole, so an omitted seed clears it), an omitted field is untouched.
/// </summary>
public record UpdatePreferencesRequest(AppearanceDto? Appearance);
