using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceSentry.Modules.Companion.Migrations
{
    /// <inheritdoc />
    public partial class M005_PushAlertDeliveries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "EventId",
                schema: "companion",
                table: "push_deliveries",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "AlertId",
                schema: "companion",
                table: "push_deliveries",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_push_deliveries_AlertId_SubscriptionId",
                schema: "companion",
                table: "push_deliveries",
                columns: new[] { "AlertId", "SubscriptionId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_push_deliveries_AlertId_SubscriptionId",
                schema: "companion",
                table: "push_deliveries");

            migrationBuilder.Sql("DELETE FROM companion.push_deliveries WHERE \"EventId\" IS NULL;");

            migrationBuilder.DropColumn(
                name: "AlertId",
                schema: "companion",
                table: "push_deliveries");

            migrationBuilder.AlterColumn<Guid>(
                name: "EventId",
                schema: "companion",
                table: "push_deliveries",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
