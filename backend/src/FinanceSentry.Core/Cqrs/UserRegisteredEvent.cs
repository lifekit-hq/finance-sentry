namespace FinanceSentry.Core.Cqrs;

/// <summary>
/// Published once a new <c>ApplicationUser</c> row is created — password registration or first
/// Google sign-in (issue #686). Cross-module: lets a module provision per-user defaults (e.g. the
/// Companion module's notification settings) without the Auth module referencing it back.
/// </summary>
public sealed record UserRegisteredEvent(Guid UserId) : IEvent;
