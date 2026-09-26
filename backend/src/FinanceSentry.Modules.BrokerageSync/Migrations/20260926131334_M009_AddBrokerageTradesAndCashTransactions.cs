using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceSentry.Modules.BrokerageSync.Migrations
{
    /// <inheritdoc />
    public partial class M009_AddBrokerageTradesAndCashTransactions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BrokerageCashTransactions",
                schema: "brokerage_sync",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Conid = table.Column<long>(type: "bigint", nullable: true),
                    Isin = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: true),
                    Symbol = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    DateTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false),
                    TransactionType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    WithholdingTax = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: true),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    FxRateToBase = table.Column<decimal>(type: "numeric(20,8)", precision: 20, scale: 8, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BrokerageCashTransactions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BrokerageTrades",
                schema: "brokerage_sync",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    IbExecutionId = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    IbTradeId = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Conid = table.Column<long>(type: "bigint", nullable: true),
                    Isin = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: true),
                    Symbol = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    InstrumentId = table.Column<Guid>(type: "uuid", nullable: true),
                    OpenCloseIndicator = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: true),
                    OpenDateTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TradeDateTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(30,10)", precision: 30, scale: 10, nullable: false),
                    Price = table.Column<decimal>(type: "numeric(20,8)", precision: 20, scale: 8, nullable: false),
                    Proceeds = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false),
                    CostBasis = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: true),
                    RealizedPnl = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: true),
                    Commission = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: true),
                    CommissionCurrency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    Taxes = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: true),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    FxRateToBase = table.Column<decimal>(type: "numeric(20,8)", precision: 20, scale: 8, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BrokerageTrades", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BrokerageTrades_BrokerageInstruments_InstrumentId",
                        column: x => x.InstrumentId,
                        principalSchema: "brokerage_sync",
                        principalTable: "BrokerageInstruments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BrokerageCashTransactions_UserId_Provider_IdempotencyKey",
                schema: "brokerage_sync",
                table: "BrokerageCashTransactions",
                columns: new[] { "UserId", "Provider", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BrokerageTrades_InstrumentId",
                schema: "brokerage_sync",
                table: "BrokerageTrades",
                column: "InstrumentId");

            migrationBuilder.CreateIndex(
                name: "IX_BrokerageTrades_UserId_Provider_IbExecutionId",
                schema: "brokerage_sync",
                table: "BrokerageTrades",
                columns: new[] { "UserId", "Provider", "IbExecutionId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BrokerageCashTransactions",
                schema: "brokerage_sync");

            migrationBuilder.DropTable(
                name: "BrokerageTrades",
                schema: "brokerage_sync");
        }
    }
}
