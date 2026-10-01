using FinanceSentry.Core.Cqrs;

namespace FinanceSentry.Modules.Auth.Application.Commands;

public record ListPeopleQuery : IQuery<IReadOnlyList<PersonDto>>;

/// <summary>An account as the People page lists it. <see cref="Status"/> is one of <c>PersonStatus</c>'s values.</summary>
public record PersonDto(string Id, string Email, IReadOnlyList<string> Roles, string Status);
