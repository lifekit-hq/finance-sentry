using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceSentry.Modules.Auth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M019_AssetVisibility : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "HideEmptyBalances",
                schema: "auth",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "HideSmallBalances",
                schema: "auth",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "SmallBalanceThreshold",
                schema: "auth",
                table: "AspNetUsers",
                type: "numeric",
                nullable: false,
                defaultValue: 1m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HideEmptyBalances",
                schema: "auth",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "HideSmallBalances",
                schema: "auth",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "SmallBalanceThreshold",
                schema: "auth",
                table: "AspNetUsers");
        }
    }
}
