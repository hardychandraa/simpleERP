using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace SimpleERP.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddExpenseTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExpenseCategories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    IsTaxDeductible = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpenseCategories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Expenses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpenseDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ExpenseCategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ReferenceNo = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Expenses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Expenses_ExpenseCategories_ExpenseCategoryId",
                        column: x => x.ExpenseCategoryId,
                        principalTable: "ExpenseCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "ExpenseCategories",
                columns: new[] { "Id", "IsActive", "IsTaxDeductible", "Name", "SortOrder" },
                values: new object[,]
                {
                    { new Guid("00000000-0000-0000-0000-000000000201"), true, true, "Gaji", 1 },
                    { new Guid("00000000-0000-0000-0000-000000000202"), true, true, "Sewa Kendaraan", 2 },
                    { new Guid("00000000-0000-0000-0000-000000000203"), true, true, "BBM", 3 },
                    { new Guid("00000000-0000-0000-0000-000000000204"), true, true, "Service Kendaraan", 4 },
                    { new Guid("00000000-0000-0000-0000-000000000205"), true, true, "ATK", 5 },
                    { new Guid("00000000-0000-0000-0000-000000000206"), true, true, "Listrik", 6 },
                    { new Guid("00000000-0000-0000-0000-000000000207"), true, true, "Air", 7 },
                    { new Guid("00000000-0000-0000-0000-000000000208"), true, true, "Telepon", 8 },
                    { new Guid("00000000-0000-0000-0000-000000000209"), true, true, "Admin Bank", 9 },
                    { new Guid("00000000-0000-0000-0000-00000000020a"), true, false, "Bunga & Denda Pajak", 10 },
                    { new Guid("00000000-0000-0000-0000-00000000020b"), true, true, "Lain-lain", 99 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseCategories_Name",
                table: "ExpenseCategories",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_ExpenseCategoryId",
                table: "Expenses",
                column: "ExpenseCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_ExpenseDate_ExpenseCategoryId",
                table: "Expenses",
                columns: new[] { "ExpenseDate", "ExpenseCategoryId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Expenses");

            migrationBuilder.DropTable(
                name: "ExpenseCategories");
        }
    }
}
