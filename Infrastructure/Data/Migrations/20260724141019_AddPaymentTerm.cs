using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace SimpleERP.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentTerm : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PaymentTermId",
                table: "Sales",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PaymentTerms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    DueDays = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentTerms", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "PaymentTerms",
                columns: new[] { "Id", "DueDays", "IsActive", "Name", "SortOrder" },
                values: new object[,]
                {
                    { new Guid("00000000-0000-0000-0000-000000000101"), 0, true, "COD", 1 },
                    { new Guid("00000000-0000-0000-0000-000000000102"), 30, true, "TOP 30", 2 },
                    { new Guid("00000000-0000-0000-0000-000000000103"), 45, true, "TOP 45", 3 },
                    { new Guid("00000000-0000-0000-0000-000000000104"), 60, true, "TOP 60", 4 },
                    { new Guid("00000000-0000-0000-0000-000000000105"), 90, true, "TOP 90", 5 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Sales_PaymentTermId",
                table: "Sales",
                column: "PaymentTermId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentTerms_Name",
                table: "PaymentTerms",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Sales_PaymentTerms_PaymentTermId",
                table: "Sales",
                column: "PaymentTermId",
                principalTable: "PaymentTerms",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Sales_PaymentTerms_PaymentTermId",
                table: "Sales");

            migrationBuilder.DropTable(
                name: "PaymentTerms");

            migrationBuilder.DropIndex(
                name: "IX_Sales_PaymentTermId",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "PaymentTermId",
                table: "Sales");
        }
    }
}
