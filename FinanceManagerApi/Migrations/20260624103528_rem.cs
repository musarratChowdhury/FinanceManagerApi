using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceManagerApi.Migrations
{
    /// <inheritdoc />
    public partial class rem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Incomes_IncomeCategories_IncomeCategoryId",
                table: "Incomes");

            migrationBuilder.AlterColumn<long>(
                name: "IncomeCategoryId",
                table: "Incomes",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.CreateTable(
                name: "IdentityPasskeyData",
                columns: table => new
                {
                    PublicKey = table.Column<byte[]>(type: "bytea", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SignCount = table.Column<long>(type: "bigint", nullable: false),
                    Transports = table.Column<string[]>(type: "text[]", nullable: true),
                    IsUserVerified = table.Column<bool>(type: "boolean", nullable: false),
                    IsBackupEligible = table.Column<bool>(type: "boolean", nullable: false),
                    IsBackedUp = table.Column<bool>(type: "boolean", nullable: false),
                    AttestationObject = table.Column<byte[]>(type: "bytea", nullable: false),
                    ClientDataJson = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                });

            migrationBuilder.CreateTable(
                name: "UserPasskeys",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "text", nullable: true),
                    CredentialId = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                });

            migrationBuilder.AddForeignKey(
                name: "FK_Incomes_IncomeCategories_IncomeCategoryId",
                table: "Incomes",
                column: "IncomeCategoryId",
                principalTable: "IncomeCategories",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Incomes_IncomeCategories_IncomeCategoryId",
                table: "Incomes");

            migrationBuilder.DropTable(
                name: "IdentityPasskeyData");

            migrationBuilder.DropTable(
                name: "UserPasskeys");

            migrationBuilder.AlterColumn<long>(
                name: "IncomeCategoryId",
                table: "Incomes",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Incomes_IncomeCategories_IncomeCategoryId",
                table: "Incomes",
                column: "IncomeCategoryId",
                principalTable: "IncomeCategories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
