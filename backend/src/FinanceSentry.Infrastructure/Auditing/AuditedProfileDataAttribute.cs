namespace FinanceSentry.Infrastructure.Auditing;

/// <summary>
/// Marks an entity as declared profile data (income, goals, career events): every save of one is put on the audit
/// trail by <see cref="AuditedWriteSaveChangesInterceptor"/>, with the field names it touched and never their values.
/// The entity must be <c>IUserOwned</c> - the trail needs to know whose data it is. <paramref name="resourceType"/> is
/// the name the trail records (<c>income_source</c>), an identifier in the sense of <c>AuditEntry</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class AuditedProfileDataAttribute(string resourceType) : Attribute
{
    public string ResourceType { get; } = resourceType;
}
