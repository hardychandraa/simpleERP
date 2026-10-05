using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SimpleERP.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class InvoicePrintFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "SalesPersons",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Unit",
                table: "Products",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "PCS");

            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "Branches",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PaperLines",
                table: "AppSettings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.UpdateData(
                table: "AppSettings",
                keyColumn: "Id",
                keyValue: "default",
                columns: new[] { "PaperColumns", "PaperLines", "VatRate" },
                values: new object[] { 96, 33, 0.11m });

            migrationBuilder.UpdateData(
                table: "Branches",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000001"),
                column: "Code",
                value: null);

            migrationBuilder.CreateIndex(
                name: "IX_SalesPersons_Code",
                table: "SalesPersons",
                column: "Code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SalesPersons_Code",
                table: "SalesPersons");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "SalesPersons");

            migrationBuilder.DropColumn(
                name: "Unit",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "PaperLines",
                table: "AppSettings");

            migrationBuilder.UpdateData(
                table: "AppSettings",
                keyColumn: "Id",
                keyValue: "default",
                columns: new[] { "PaperColumns", "VatRate" },
                values: new object[] { 80, 0.10m });
        }
    }
}
