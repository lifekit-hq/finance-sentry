using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceSentry.Modules.Auth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M013_FireProjectionAssumptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "RealAnnualReturn",
                schema: "auth",
                table: "AspNetUsers",
                type: "numeric",
                nullable: false,
                defaultValue: 0.05m);

            migrationBuilder.AddColumn<decimal>(
                name: "SafeWithdrawalRate",
                schema: "auth",
                table: "AspNetUsers",
                type: "numeric",
                nullable: false,
                defaultValue: 0.04m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RealAnnualReturn",
                schema: "auth",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "SafeWithdrawalRate",
                schema: "auth",
                table: "AspNetUsers");
        }
    }
}
