using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SimpleERP.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class CustomerDefaultsAndLedgerEnteredAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "EnteredAt",
                table: "InventoryLedgers",
                type: "timestamp without time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            // Rows written so far were always dated "now", so TransactionDate already is
            // their posting time, and the moving-average lookup keeps picking the same row.
            migrationBuilder.Sql(@"UPDATE ""InventoryLedgers"" SET ""EnteredAt"" = ""TransactionDate"";");

            migrationBuilder.AddColumn<decimal>(
                name: "DefaultDiscountPercent",
                table: "Customers",
                type: "numeric(18,4)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PaymentTermId",
                table: "Customers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SalesPersonId",
                table: "Customers",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Customers_PaymentTermId",
                table: "Customers",
                column: "PaymentTermId");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_SalesPersonId",
                table: "Customers",
                column: "SalesPersonId");

            migrationBuilder.AddForeignKey(
                name: "FK_Customers_PaymentTerms_PaymentTermId",
                table: "Customers",
                column: "PaymentTermId",
                principalTable: "PaymentTerms",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Customers_SalesPersons_SalesPersonId",
                table: "Customers",
                column: "SalesPersonId",
                principalTable: "SalesPersons",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Customers_PaymentTerms_PaymentTermId",
                table: "Customers");

            migrationBuilder.DropForeignKey(
                name: "FK_Customers_SalesPersons_SalesPersonId",
                table: "Customers");

            migrationBuilder.DropIndex(
                name: "IX_Customers_PaymentTermId",
                table: "Customers");

            migrationBuilder.DropIndex(
                name: "IX_Customers_SalesPersonId",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "EnteredAt",
                table: "InventoryLedgers");

            migrationBuilder.DropColumn(
                name: "DefaultDiscountPercent",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "PaymentTermId",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "SalesPersonId",
                table: "Customers");
        }
    }
}
