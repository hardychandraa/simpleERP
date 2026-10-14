using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SimpleERP.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class StockCounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StockCounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CountNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    Scope = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    SubmittedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ClosedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ClosedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockCounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StockCountLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StockCountId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    CountedQty = table.Column<int>(type: "integer", nullable: true),
                    SystemQty = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    Note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Decision = table.Column<int>(type: "integer", nullable: false),
                    DecisionNote = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    DecidedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    DecidedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    StockAdjustmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    AdjustedQty = table.Column<decimal>(type: "numeric(18,4)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockCountLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockCountLines_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockCountLines_StockCounts_StockCountId",
                        column: x => x.StockCountId,
                        principalTable: "StockCounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StockCountLines_ProductId",
                table: "StockCountLines",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_StockCountLines_StockCountId_ProductId",
                table: "StockCountLines",
                columns: new[] { "StockCountId", "ProductId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockCounts_CountNumber",
                table: "StockCounts",
                column: "CountNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockCounts_Status",
                table: "StockCounts",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_StockCounts_SubmittedAt",
                table: "StockCounts",
                column: "SubmittedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StockCountLines");

            migrationBuilder.DropTable(
                name: "StockCounts");
        }
    }
}
