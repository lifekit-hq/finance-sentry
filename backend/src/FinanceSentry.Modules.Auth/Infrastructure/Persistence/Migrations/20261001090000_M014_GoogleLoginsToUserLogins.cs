using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceSentry.Modules.Auth.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Data only, additive: Google sign-in now resolves through Identity's external-login table, so every
    /// linked Google subject in the legacy <c>GoogleId</c> column is copied there. The column stays; nothing
    /// is dropped or rewritten, and a re-run inserts nothing new.
    /// </summary>
    public partial class M014_GoogleLoginsToUserLogins : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                INSERT INTO auth."AspNetUserLogins" ("LoginProvider", "ProviderKey", "ProviderDisplayName", "UserId")
                SELECT 'Google', u."GoogleId", 'Google', u."Id"
                FROM auth."AspNetUsers" u
                WHERE u."GoogleId" IS NOT NULL AND u."GoogleId" <> ''
                ON CONFLICT ("LoginProvider", "ProviderKey") DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Rows copied by Up are indistinguishable from links made since; leave external logins in place.
        }
    }
}
