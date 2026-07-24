using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SimpleERP.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceLevelDiscount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "InvoiceDiscountAmount",
                table: "Sales",
                type: "numeric(18,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "InvoiceDiscountPercent",
                table: "Sales",
                type: "numeric(18,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AllocatedInvoiceDiscount",
                table: "SaleItems",
                type: "numeric(18,4)",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InvoiceDiscountAmount",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "InvoiceDiscountPercent",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "AllocatedInvoiceDiscount",
                table: "SaleItems");
        }
    }
}
