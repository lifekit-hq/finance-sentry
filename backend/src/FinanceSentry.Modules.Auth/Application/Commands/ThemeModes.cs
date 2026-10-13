namespace FinanceSentry.Modules.Auth.Application.Commands;

/// <summary>The light/dark choices a person can store; <see cref="System"/> follows the OS and is the default.</summary>
public static class ThemeModes
{
    public const string System = "system";
    public const string Light = "light";
    public const string Dark = "dark";

    public static bool IsKnown(string? value) => value is System or Light or Dark;
}
