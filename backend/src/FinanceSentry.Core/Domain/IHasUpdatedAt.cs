namespace FinanceSentry.Core.Domain;

/// <summary>
/// Marks an entity whose <c>UpdatedAt</c> column is stamped by the persistence layer whenever the
/// entity is saved in the Modified state, so domain methods and repositories never assign it.
/// The property is found by name and may be <see cref="DateTime"/>, <see cref="Nullable{DateTime}"/>
/// or <see cref="DateTimeOffset"/>; the interceptor writes the matching type.
/// </summary>
/// <remarks>
/// Set-based updates (<c>ExecuteUpdate</c>) bypass SaveChanges and must keep their explicit
/// <c>SetProperty(x => x.UpdatedAt, ...)</c>.
/// </remarks>
public interface IHasUpdatedAt;
