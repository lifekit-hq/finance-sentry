using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceSentry.Modules.CryptoSync.Migrations
{
    /// <inheritdoc />
    public partial class M010_CryptoFlowEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "FlowsWalkedTo",
                schema: "crypto_sync",
                table: "ExchangeCredentials",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CryptoFlowEvents",
                schema: "crypto_sync",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    EventId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ProviderType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(30,10)", precision: 30, scale: 10, nullable: false),
                    Unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    IsFiat = table.Column<bool>(type: "boolean", nullable: false),
                    UnitPriceUsd = table.Column<decimal>(type: "numeric(20,8)", precision: 20, scale: 8, nullable: true),
                    Fee = table.Column<decimal>(type: "numeric(30,10)", precision: 30, scale: 10, nullable: true),
                    FeeUnit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    CounterpartyRef = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    RecordedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CryptoFlowEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CryptoFlowEvents_UserId_Provider_ProviderType_EventId",
                schema: "crypto_sync",
                table: "CryptoFlowEvents",
                columns: new[] { "UserId", "Provider", "ProviderType", "EventId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CryptoFlowEvents",
                schema: "crypto_sync");

            migrationBuilder.DropColumn(
                name: "FlowsWalkedTo",
                schema: "crypto_sync",
                table: "ExchangeCredentials");
        }
    }
}
