using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace FinanceSentry.Modules.Companion.Migrations
{
    /// <inheritdoc />
    public partial class M007_LedgerCorrections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ledger_corrections",
                schema: "companion",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AgentSaid = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    WhatWasWrong = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    OwnerCorrection = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CorrectedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ledger_corrections", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ledger_corrections_UserId_CorrectedAt",
                schema: "companion",
                table: "ledger_corrections",
                columns: new[] { "UserId", "CorrectedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ledger_corrections",
                schema: "companion");
        }
    }
}
