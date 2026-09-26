using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceSentry.Modules.BankSync.Migrations
{
    /// <inheritdoc />
    public partial class M020_TransactionUserActivePostedDateIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "idx_transaction_user_active_posted_date",
                schema: "bank_sync",
                table: "Transactions",
                columns: new[] { "UserId", "IsActive", "PostedDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_transaction_user_active_posted_date",
                schema: "bank_sync",
                table: "Transactions");
        }
    }
}
