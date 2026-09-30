using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceSentry.Modules.Analytics.Migrations
{
    /// <summary>
    /// Keeps the analytics owner scope out of reach of the query it scopes. The curated views filter on
    /// the transaction-local <c>app.current_user_id</c> setting, which the executor applies with
    /// <c>set_config</c> as the app login before dropping to <c>fs_readonly</c>. PostgreSQL grants
    /// <c>EXECUTE</c> on <c>set_config</c> to PUBLIC by default, so <c>fs_readonly</c> could call it too;
    /// this revokes it from PUBLIC and grants it back only to the app login that runs the setup.
    /// <para>
    /// Privilege-only: no data changes, and both statements are idempotent, so re-running is safe. Only a
    /// superuser can change the ACL of a <c>pg_catalog</c> function; on a non-superuser login the change is
    /// skipped with a warning (the SQL guard still rejects setting functions). A dedicated read-only login,
    /// if one replaces the shared app login, needs the same <c>GRANT EXECUTE</c>.
    /// </para>
    /// </summary>
    public partial class M002_RestrictSetConfigExecute : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DO $$
BEGIN
    IF (SELECT rolsuper FROM pg_roles WHERE rolname = CURRENT_USER) THEN
        REVOKE EXECUTE ON FUNCTION pg_catalog.set_config(text, text, boolean) FROM PUBLIC;
        EXECUTE format('GRANT EXECUTE ON FUNCTION pg_catalog.set_config(text, text, boolean) TO %I', CURRENT_USER);
    ELSE
        RAISE WARNING 'set_config EXECUTE left granted to PUBLIC: % is not a superuser', CURRENT_USER;
    END IF;
END
$$;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DO $$
BEGIN
    IF (SELECT rolsuper FROM pg_roles WHERE rolname = CURRENT_USER) THEN
        GRANT EXECUTE ON FUNCTION pg_catalog.set_config(text, text, boolean) TO PUBLIC;
        EXECUTE format('REVOKE EXECUTE ON FUNCTION pg_catalog.set_config(text, text, boolean) FROM %I', CURRENT_USER);
    END IF;
END
$$;
");
        }
    }
}
