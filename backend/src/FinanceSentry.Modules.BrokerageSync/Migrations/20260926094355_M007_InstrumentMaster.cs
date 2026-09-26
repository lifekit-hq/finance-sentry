using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceSentry.Modules.BrokerageSync.Migrations
{
    /// <inheritdoc />
    public partial class M007_InstrumentMaster : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "InstrumentId",
                schema: "brokerage_sync",
                table: "BrokerageHoldings",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BrokerageInstruments",
                schema: "brokerage_sync",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Conid = table.Column<long>(type: "bigint", nullable: false),
                    Isin = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: true),
                    Symbol = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    InstrumentType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Classification = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BrokerageInstruments", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BrokerageHoldings_InstrumentId",
                schema: "brokerage_sync",
                table: "BrokerageHoldings",
                column: "InstrumentId");

            migrationBuilder.CreateIndex(
                name: "IX_BrokerageInstruments_UserId_Provider_Conid",
                schema: "brokerage_sync",
                table: "BrokerageInstruments",
                columns: new[] { "UserId", "Provider", "Conid" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_BrokerageHoldings_BrokerageInstruments_InstrumentId",
                schema: "brokerage_sync",
                table: "BrokerageHoldings",
                column: "InstrumentId",
                principalSchema: "brokerage_sync",
                principalTable: "BrokerageInstruments",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BrokerageHoldings_BrokerageInstruments_InstrumentId",
                schema: "brokerage_sync",
                table: "BrokerageHoldings");

            migrationBuilder.DropTable(
                name: "BrokerageInstruments",
                schema: "brokerage_sync");

            migrationBuilder.DropIndex(
                name: "IX_BrokerageHoldings_InstrumentId",
                schema: "brokerage_sync",
                table: "BrokerageHoldings");

            migrationBuilder.DropColumn(
                name: "InstrumentId",
                schema: "brokerage_sync",
                table: "BrokerageHoldings");
        }
    }
}
