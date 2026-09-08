using System;
using FinanceSentry.Modules.BankSync.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceSentry.Modules.BankSync.Migrations
{
    /// <summary>
    /// The per-user merchant pins behind rule (d) of the committed-outflow policy (spec 554).
    /// Empty on creation — a pin is a statement only the user can make, so there is nothing to
    /// seed.
    /// </summary>
    [DbContext(typeof(BankSyncDbContext))]
    [Migration("20260908120000_M017_CommittedMerchantPins")]
    public partial class M017_CommittedMerchantPins : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "committed_merchant_pins",
                schema: "bank_sync",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    MerchantKey = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_committed_merchant_pins", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_committed_merchant_pin_user_merchant_unique",
                schema: "bank_sync",
                table: "committed_merchant_pins",
                columns: ["UserId", "MerchantKey"],
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "committed_merchant_pins",
                schema: "bank_sync");
        }
    }
}
