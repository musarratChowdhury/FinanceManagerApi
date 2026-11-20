using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceManagerApi.Migrations
{
    /// <inheritdoc />
    public partial class AddReceiptDetailsAndFractionalQuantity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "GrandTotal",
                table: "Receipts",
                newName: "NetPayable");

            migrationBuilder.RenameColumn(
                name: "ExpenseDate",
                table: "Receipts",
                newName: "ReceiptDateTime");

            migrationBuilder.AddColumn<double>(
                name: "Discount",
                table: "Receipts",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "GrossTotal",
                table: "Receipts",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<string>(
                name: "InvoiceNo",
                table: "Receipts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Merchant",
                table: "Receipts",
                type: "text",
                nullable: true);

            migrationBuilder.AlterColumn<double>(
                name: "Quantity",
                table: "Expenses",
                type: "double precision",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Discount",
                table: "Receipts");

            migrationBuilder.DropColumn(
                name: "GrossTotal",
                table: "Receipts");

            migrationBuilder.DropColumn(
                name: "InvoiceNo",
                table: "Receipts");

            migrationBuilder.DropColumn(
                name: "Merchant",
                table: "Receipts");

            migrationBuilder.RenameColumn(
                name: "ReceiptDateTime",
                table: "Receipts",
                newName: "ExpenseDate");

            migrationBuilder.RenameColumn(
                name: "NetPayable",
                table: "Receipts",
                newName: "GrandTotal");

            migrationBuilder.AlterColumn<int>(
                name: "Quantity",
                table: "Expenses",
                type: "integer",
                nullable: true,
                oldClrType: typeof(double),
                oldType: "double precision",
                oldNullable: true);
        }
    }
}
