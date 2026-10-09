using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceSentry.Modules.BrokerageSync.Migrations
{
    /// <summary>
    /// The owner hands over his Inzhur session instead of finance-sentry signing in (the sign-in's reCAPTCHA rejects
    /// the server's IP): the stored phone number and password, their key version and the daily sign-in counter go.
    /// </summary>
    public partial class M013_InzhurSessionHandover : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EncryptedPhone",
                schema: "brokerage_sync",
                table: "InzhurCredentials");

            migrationBuilder.DropColumn(
                name: "PhoneIv",
                schema: "brokerage_sync",
                table: "InzhurCredentials");

            migrationBuilder.DropColumn(
                name: "PhoneAuthTag",
                schema: "brokerage_sync",
                table: "InzhurCredentials");

            migrationBuilder.DropColumn(
                name: "EncryptedPassword",
                schema: "brokerage_sync",
                table: "InzhurCredentials");

            migrationBuilder.DropColumn(
                name: "PasswordIv",
                schema: "brokerage_sync",
                table: "InzhurCredentials");

            migrationBuilder.DropColumn(
                name: "PasswordAuthTag",
                schema: "brokerage_sync",
                table: "InzhurCredentials");

            migrationBuilder.DropColumn(
                name: "KeyVersion",
                schema: "brokerage_sync",
                table: "InzhurCredentials");

            migrationBuilder.DropColumn(
                name: "LoginAttemptsDay",
                schema: "brokerage_sync",
                table: "InzhurCredentials");

            migrationBuilder.DropColumn(
                name: "LoginAttempts",
                schema: "brokerage_sync",
                table: "InzhurCredentials");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "EncryptedPhone",
                schema: "brokerage_sync",
                table: "InzhurCredentials",
                type: "bytea",
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<byte[]>(
                name: "PhoneIv",
                schema: "brokerage_sync",
                table: "InzhurCredentials",
                type: "bytea",
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<byte[]>(
                name: "PhoneAuthTag",
                schema: "brokerage_sync",
                table: "InzhurCredentials",
                type: "bytea",
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<byte[]>(
                name: "EncryptedPassword",
                schema: "brokerage_sync",
                table: "InzhurCredentials",
                type: "bytea",
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<byte[]>(
                name: "PasswordIv",
                schema: "brokerage_sync",
                table: "InzhurCredentials",
                type: "bytea",
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<byte[]>(
                name: "PasswordAuthTag",
                schema: "brokerage_sync",
                table: "InzhurCredentials",
                type: "bytea",
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<int>(
                name: "KeyVersion",
                schema: "brokerage_sync",
                table: "InzhurCredentials",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<DateOnly>(
                name: "LoginAttemptsDay",
                schema: "brokerage_sync",
                table: "InzhurCredentials",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LoginAttempts",
                schema: "brokerage_sync",
                table: "InzhurCredentials",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }
    }
}
