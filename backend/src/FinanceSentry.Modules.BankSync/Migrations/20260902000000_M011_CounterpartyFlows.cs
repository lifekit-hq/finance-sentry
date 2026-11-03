using FinanceSentry.Modules.BankSync.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceSentry.Modules.BankSync.Migrations
{
    // Hand-written (no dotnet-ef in this repo's toolchain), so the attributes the designer
    // file would normally carry live here. Without [Migration] the migrator does not
    // discover this migration at all and the counterparty tables are never created.
    [DbContext(typeof(BankSyncDbContext))]
    [Migration("20260902000000_M011_CounterpartyFlows")]
    public partial class M011_CounterpartyFlows : Migration
    {
        // Well-known GUIDs for default counterparties (UserId = Guid.Empty = system defaults).
        private static readonly Guid LyudmilaId = new("11111111-0000-0000-0000-000000000001");
        private static readonly Guid MariiaId = new("11111111-0000-0000-0000-000000000002");

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "counterparties",
                schema: "bank_sync",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    FlowRole = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false,
                        defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_counterparties", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_counterparty_user_id",
                schema: "bank_sync",
                table: "counterparties",
                column: "UserId");

            migrationBuilder.CreateTable(
                name: "counterparty_rules",
                schema: "bank_sync",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CounterpartyId = table.Column<Guid>(type: "uuid", nullable: false),
                    MatchType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Pattern = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    CreatedAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false,
                        defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_counterparty_rules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_counterparty_rules_counterparties_CounterpartyId",
                        column: x => x.CounterpartyId,
                        principalSchema: "bank_sync",
                        principalTable: "counterparties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_counterparty_rule_counterparty_id",
                schema: "bank_sync",
                table: "counterparty_rules",
                column: "CounterpartyId");

            // Seed FAMILY_SUPPORT into the categories reference table. Raw SQL rather than
            // InsertData: the row may already exist (production got it during the 2026-08
            // family-flows recategorization) and InsertData cannot express ON CONFLICT — a
            // duplicate key would roll back the entire migration on every startup.
            migrationBuilder.Sql(
                """
                INSERT INTO bank_sync.categories ("Key", "Label", "SortOrder")
                VALUES ('FAMILY_SUPPORT', 'Family Support', 135)
                ON CONFLICT ("Key") DO NOTHING;
                """);

            // Seed system-default counterparties (UserId = Guid.Empty applies to all users).
            migrationBuilder.InsertData(
                schema: "bank_sync",
                table: "counterparties",
                columns: ["Id", "UserId", "Name", "FlowRole", "CreatedAt"],
                columnTypes: ["uuid", "uuid", "character varying(255)", "character varying(50)", "timestamp with time zone"],
                values: new object[,]
                {
                    {
                        LyudmilaId,
                        Guid.Empty,
                        "Оксана Лисенко (Мама)",
                        "family_support",
                        new DateTime(2026, 9, 2, 0, 0, 0, DateTimeKind.Utc)
                    },
                    {
                        MariiaId,
                        Guid.Empty,
                        "Марія Коваль (Марічка)",
                        "family_support",
                        new DateTime(2026, 9, 2, 0, 0, 0, DateTimeKind.Utc)
                    }
                });

            // Seed match rules for the default counterparties.
            migrationBuilder.InsertData(
                schema: "bank_sync",
                table: "counterparty_rules",
                columns: ["Id", "CounterpartyId", "MatchType", "Pattern", "CreatedAt"],
                columnTypes: ["uuid", "uuid", "character varying(50)", "character varying(255)", "timestamp with time zone"],
                values: new object[,]
                {
                    // Оксана Лисенко — matches description "Оксана Лисенко" or "мама"
                    {
                        new Guid("22222222-0000-0000-0000-000000000001"),
                        LyudmilaId,
                        "description_contains",
                        "Оксана Лисенко",
                        new DateTime(2026, 9, 2, 0, 0, 0, DateTimeKind.Utc)
                    },
                    {
                        new Guid("22222222-0000-0000-0000-000000000002"),
                        LyudmilaId,
                        "description_contains",
                        "мама",
                        new DateTime(2026, 9, 2, 0, 0, 0, DateTimeKind.Utc)
                    },
                    // Марія Коваль — matches description "Марія Коваль" or "Марічка"
                    {
                        new Guid("22222222-0000-0000-0000-000000000003"),
                        MariiaId,
                        "description_contains",
                        "Марія Коваль",
                        new DateTime(2026, 9, 2, 0, 0, 0, DateTimeKind.Utc)
                    },
                    {
                        new Guid("22222222-0000-0000-0000-000000000004"),
                        MariiaId,
                        "description_contains",
                        "Марічка",
                        new DateTime(2026, 9, 2, 0, 0, 0, DateTimeKind.Utc)
                    }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "bank_sync",
                table: "categories",
                keyColumn: "Key",
                keyColumnType: "character varying",
                keyValue: "FAMILY_SUPPORT");

            migrationBuilder.DropTable(
                name: "counterparty_rules",
                schema: "bank_sync");

            migrationBuilder.DropTable(
                name: "counterparties",
                schema: "bank_sync");
        }
    }
}
