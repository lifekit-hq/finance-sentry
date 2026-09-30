namespace FinanceSentry.Modules.Auth.Infrastructure.Identity;

using System.Collections.Frozen;
using FinanceSentry.Modules.Auth.Domain.Entities;
using Microsoft.AspNetCore.Identity;

/// <summary>
/// Identity password validator that rejects passwords on a list of commonly used ones (the SecLists
/// 10,000 most common, embedded as <c>common-passwords.txt</c>), compared case-insensitively. It runs
/// wherever Identity validates a new password: account creation, password change and reset.
/// </summary>
public sealed class CommonPasswordValidator : IPasswordValidator<ApplicationUser>
{
    public const string ErrorCode = "CommonPassword";
    public const string ErrorDescription = "This password is too common. Choose a less predictable one.";

    private const string ResourceName = "FinanceSentry.Modules.Auth.CommonPasswords.txt";

    private static readonly Lazy<FrozenSet<string>> CommonPasswords = new(Load);

    public Task<IdentityResult> ValidateAsync(UserManager<ApplicationUser> manager, ApplicationUser user, string? password) =>
        Task.FromResult(password is not null && CommonPasswords.Value.Contains(password)
            ? IdentityResult.Failed(new IdentityError { Code = ErrorCode, Description = ErrorDescription })
            : IdentityResult.Success);

    private static FrozenSet<string> Load()
    {
        using var stream = typeof(CommonPasswordValidator).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource {ResourceName} is missing.");
        using var reader = new StreamReader(stream);

        var passwords = new List<string>();
        while (reader.ReadLine() is { } line)
        {
            if (line.Length > 0 && !line.StartsWith('#'))
                passwords.Add(line);
        }

        return passwords.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
    }
}
