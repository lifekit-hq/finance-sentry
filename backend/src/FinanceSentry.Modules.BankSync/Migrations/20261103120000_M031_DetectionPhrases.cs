using FinanceSentry.Modules.BankSync.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace FinanceSentry.Modules.BankSync.Migrations
{
    /// <summary>
    /// Statement vocabulary as data (release-readiness item 5): the wording the detection code held in C#
    /// constants - installment markers, directional transfer prefixes, the "not a merchant" words, the AIB
    /// ATM-lodgement marker - becomes rows of <c>bank_sync.detection_phrases</c>. The rows are inserted here
    /// as well as by <c>CategorySeeder</c>, so a host that reads the table before the API has seeded it
    /// (the MCP host after a deploy) never sees it empty. The values are frozen copies of what shipped
    /// at this migration, in the order the code tried them (row id ascending).
    /// </summary>
    [DbContext(typeof(BankSyncDbContext))]
    [Migration("20261103120000_M031_DetectionPhrases")]
    public partial class M031_DetectionPhrases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "detection_phrases",
                schema: "bank_sync",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Kind = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Phrase = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Locale = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    MccCondition = table.Column<int>(type: "integer", nullable: true),
                    CategoryCondition = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_detection_phrases", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_detection_phrase_kind_phrase_unique",
                schema: "bank_sync",
                table: "detection_phrases",
                columns: new[] { "Kind", "Phrase" },
                unique: true);

            migrationBuilder.Sql(@"
                INSERT INTO bank_sync.detection_phrases (""Kind"", ""Phrase"", ""Provider"", ""Locale"", ""MccCondition"", ""CategoryCondition"")
                VALUES
                ('not_a_merchant', 'unknown', NULL, 'en', NULL, NULL),
                ('not_a_merchant', 'transfer', NULL, 'en', NULL, NULL),
                ('not_a_merchant', 'top-up', NULL, 'en', NULL, NULL),
                ('not_a_merchant', 'topup', NULL, 'en', NULL, NULL),
                ('not_a_merchant', 'recharge', NULL, 'en', NULL, NULL),
                ('not_a_merchant', 'withdrawal', NULL, 'en', NULL, NULL),
                ('not_a_merchant', 'atm', NULL, 'en', NULL, NULL),
                ('not_a_merchant', 'cash', NULL, 'en', NULL, NULL),
                ('not_a_merchant', 'переказ', NULL, 'uk', NULL, NULL),
                ('installment_marker', 'погашення наступного платежу', 'monobank', 'uk', NULL, NULL),
                ('installment_marker', 'повне погашення', 'monobank', 'uk', NULL, NULL),
                ('installment_marker', 'щомісячний платіж', 'monobank', 'uk', NULL, NULL),
                ('installment_marker', 'monomarket', 'monobank', NULL, NULL, NULL),
                ('installment_marker', 'розстроч', 'monobank', 'uk', NULL, NULL),
                ('installment_marker', 'оплата частинами', 'monobank', 'uk', NULL, NULL),
                ('installment_marker', 'покупка частинами', 'monobank', 'uk', NULL, NULL),
                ('installment_marker', 'частинами', 'monobank', 'uk', NULL, NULL),
                ('installment_marker', 'installment', NULL, 'en', NULL, NULL),
                ('installment_payoff_marker', 'повне погашення', 'monobank', 'uk', NULL, NULL),
                ('installment_merchant_prefix', 'погашення наступного платежу', 'monobank', 'uk', NULL, NULL),
                ('installment_merchant_prefix', 'повне погашення', 'monobank', 'uk', NULL, NULL),
                ('installment_merchant_prefix', 'щомісячний платіж', 'monobank', 'uk', NULL, NULL),
                ('installment_merchant_prefix', 'платіж', 'monobank', 'uk', NULL, NULL),
                ('installment_merchant_tag', 'monomarket', 'monobank', NULL, NULL, NULL),
                ('installment_transfer_prefix', 'Платіж ', 'monobank', 'uk', 4829, NULL),
                ('income_prefix', 'Payment from ', 'truelayer', 'en', NULL, NULL),
                ('transfer_in_prefix', 'From ', 'truelayer', 'en', NULL, NULL),
                ('transfer_in_prefix', 'З банки ', 'monobank', 'uk', NULL, NULL),
                ('transfer_out_prefix', 'To ', 'truelayer', 'en', NULL, NULL),
                ('transfer_out_prefix', 'Поповнення ', 'monobank', 'uk', NULL, NULL),
                ('cash_lodgement_marker', '*ATMLDG', 'aib', 'en', NULL, NULL);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "detection_phrases",
                schema: "bank_sync");
        }
    }
}
