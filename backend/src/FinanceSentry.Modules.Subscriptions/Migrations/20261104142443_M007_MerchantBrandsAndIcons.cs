using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace FinanceSentry.Modules.Subscriptions.Migrations
{
    /// <summary>
    /// Merchant icons (issue #1225): <c>merchant_brands</c> is the curated merchant-to-host table the
    /// icon lookup reads (seeded by <c>MerchantBrandSeeder</c> at startup, like <c>detection_phrases</c>),
    /// and <c>merchant_icons</c> is the server-side cache of the favicons fetched from those hosts,
    /// negative results included. Two new tables, nothing existing is altered.
    /// </summary>
    public partial class M007_MerchantBrandsAndIcons : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "merchant_brands",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Keyword = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Domain = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_merchant_brands", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "merchant_icons",
                columns: table => new
                {
                    Domain = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Content = table.Column<byte[]>(type: "bytea", nullable: true),
                    FetchedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_merchant_icons", x => x.Domain);
                });

            migrationBuilder.CreateIndex(
                name: "idx_merchant_brand_keyword_unique",
                table: "merchant_brands",
                column: "Keyword",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "merchant_brands");

            migrationBuilder.DropTable(
                name: "merchant_icons");
        }
    }
}
