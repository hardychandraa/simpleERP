using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SimpleERP.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCommissionEngine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CommissionPayouts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SalesPersonId = table.Column<Guid>(type: "uuid", nullable: false),
                    PayoutDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommissionPayouts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommissionPayouts_SalesPersons_SalesPersonId",
                        column: x => x.SalesPersonId,
                        principalTable: "SalesPersons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CommissionRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SalesPersonId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: true),
                    Category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Rate = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    EffectiveFrom = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    EffectiveTo = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommissionRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommissionRules_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CommissionRules_SalesPersons_SalesPersonId",
                        column: x => x.SalesPersonId,
                        principalTable: "SalesPersons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CommissionAccruals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SaleId = table.Column<Guid>(type: "uuid", nullable: false),
                    SaleItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentRecordId = table.Column<Guid>(type: "uuid", nullable: true),
                    SalesPersonId = table.Column<Guid>(type: "uuid", nullable: false),
                    CommissionRuleId = table.Column<Guid>(type: "uuid", nullable: false),
                    BaseAmount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    Rate = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    CommissionPayoutId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsVoided = table.Column<bool>(type: "boolean", nullable: false),
                    AccrualDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommissionAccruals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommissionAccruals_CommissionPayouts_CommissionPayoutId",
                        column: x => x.CommissionPayoutId,
                        principalTable: "CommissionPayouts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_CommissionAccruals_CommissionRules_CommissionRuleId",
                        column: x => x.CommissionRuleId,
                        principalTable: "CommissionRules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CommissionAccruals_SaleItems_SaleItemId",
                        column: x => x.SaleItemId,
                        principalTable: "SaleItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CommissionAccruals_SalesPersons_SalesPersonId",
                        column: x => x.SalesPersonId,
                        principalTable: "SalesPersons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CommissionAccruals_Sales_SaleId",
                        column: x => x.SaleId,
                        principalTable: "Sales",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CommissionAccruals_CommissionPayoutId",
                table: "CommissionAccruals",
                column: "CommissionPayoutId");

            migrationBuilder.CreateIndex(
                name: "IX_CommissionAccruals_CommissionRuleId",
                table: "CommissionAccruals",
                column: "CommissionRuleId");

            migrationBuilder.CreateIndex(
                name: "IX_CommissionAccruals_SaleId",
                table: "CommissionAccruals",
                column: "SaleId");

            migrationBuilder.CreateIndex(
                name: "IX_CommissionAccruals_SaleItemId",
                table: "CommissionAccruals",
                column: "SaleItemId");

            migrationBuilder.CreateIndex(
                name: "IX_CommissionAccruals_SalesPersonId_CommissionPayoutId_IsVoided",
                table: "CommissionAccruals",
                columns: new[] { "SalesPersonId", "CommissionPayoutId", "IsVoided" });

            migrationBuilder.CreateIndex(
                name: "IX_CommissionPayouts_SalesPersonId_PayoutDate",
                table: "CommissionPayouts",
                columns: new[] { "SalesPersonId", "PayoutDate" });

            migrationBuilder.CreateIndex(
                name: "IX_CommissionRules_Name",
                table: "CommissionRules",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommissionRules_ProductId",
                table: "CommissionRules",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_CommissionRules_SalesPersonId_IsActive",
                table: "CommissionRules",
                columns: new[] { "SalesPersonId", "IsActive" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CommissionAccruals");

            migrationBuilder.DropTable(
                name: "CommissionPayouts");

            migrationBuilder.DropTable(
                name: "CommissionRules");
        }
    }
}
