namespace FinanceSentry.Tests.Integration.Shared;

using Xunit;

/// <summary>
/// The classes that create cluster-wide Postgres state on a shared <see cref="PostgresServer"/>: the API's full
/// startup migrations create Analytics' <c>fs_readonly</c> role, which every database on the server sees. One
/// collection runs them one test at a time, so two of them never race on <c>CREATE ROLE</c> and each test's
/// database can drop the roles it left behind (<c>CreateDatabaseAsync(resetsClusterRoles: true)</c>) without
/// pulling one out from under another test. No class outside this collection creates a role.
/// </summary>
[CollectionDefinition(Name)]
public sealed class PostgresClusterStateCollection
{
    public const string Name = "Postgres cluster state";
}
