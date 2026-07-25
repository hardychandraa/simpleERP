using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SimpleERP.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRebateEngine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "RebateWithholdingRate",
                table: "AppSettings",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "RebateRealizations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                    RealizationDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    RewardType = table.Column<int>(type: "integer", nullable: false),
                    GrossAmount = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    WithholdingRate = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    WithholdingAmount = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    NetAmount = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    InKindProductId = table.Column<Guid>(type: "uuid", nullable: true),
                    InKindQty = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    ReferenceId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RebateRealizations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RebateRealizations_Products_InKindProductId",
                        column: x => x.InKindProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RebateRealizations_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RebateRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: true),
                    ConditionType = table.Column<int>(type: "integer", nullable: false),
                    ThresholdQty = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    ThresholdValue = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    ReferenceCost = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    OnTimePaymentDays = table.Column<int>(type: "integer", nullable: true),
                    PeriodStart = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    PeriodEnd = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    RewardType = table.Column<int>(type: "integer", nullable: false),
                    RewardRate = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    RewardAmount = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    RewardProductId = table.Column<Guid>(type: "uuid", nullable: true),
                    RewardQty = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RebateRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RebateRules_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RebateRules_Products_RewardProductId",
                        column: x => x.RewardProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RebateRules_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RebateAccruals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RebateRuleId = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                    PurchaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    PurchaseItemId = table.Column<Guid>(type: "uuid", nullable: true),
                    RewardType = table.Column<int>(type: "integer", nullable: false),
                    Qty = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    RebateRealizationId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsVoided = table.Column<bool>(type: "boolean", nullable: false),
                    AccrualDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RebateAccruals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RebateAccruals_Purchases_PurchaseId",
                        column: x => x.PurchaseId,
                        principalTable: "Purchases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RebateAccruals_RebateRealizations_RebateRealizationId",
                        column: x => x.RebateRealizationId,
                        principalTable: "RebateRealizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_RebateAccruals_RebateRules_RebateRuleId",
                        column: x => x.RebateRuleId,
                        principalTable: "RebateRules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RebateAccruals_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.UpdateData(
                table: "AppSettings",
                keyColumn: "Id",
                keyValue: "default",
                column: "RebateWithholdingRate",
                value: 0.15m);

            migrationBuilder.CreateIndex(
                name: "IX_RebateAccruals_PurchaseId",
                table: "RebateAccruals",
                column: "PurchaseId");

            migrationBuilder.CreateIndex(
                name: "IX_RebateAccruals_RebateRealizationId",
                table: "RebateAccruals",
                column: "RebateRealizationId");

            migrationBuilder.CreateIndex(
                name: "IX_RebateAccruals_RebateRuleId",
                table: "RebateAccruals",
                column: "RebateRuleId");

            migrationBuilder.CreateIndex(
                name: "IX_RebateAccruals_SupplierId_RebateRealizationId_IsVoided",
                table: "RebateAccruals",
                columns: new[] { "SupplierId", "RebateRealizationId", "IsVoided" });

            migrationBuilder.CreateIndex(
                name: "IX_RebateRealizations_InKindProductId",
                table: "RebateRealizations",
                column: "InKindProductId");

            migrationBuilder.CreateIndex(
                name: "IX_RebateRealizations_SupplierId_RealizationDate",
                table: "RebateRealizations",
                columns: new[] { "SupplierId", "RealizationDate" });

            migrationBuilder.CreateIndex(
                name: "IX_RebateRules_Name",
                table: "RebateRules",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RebateRules_ProductId",
                table: "RebateRules",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_RebateRules_RewardProductId",
                table: "RebateRules",
                column: "RewardProductId");

            migrationBuilder.CreateIndex(
                name: "IX_RebateRules_SupplierId_IsActive",
                table: "RebateRules",
                columns: new[] { "SupplierId", "IsActive" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RebateAccruals");

            migrationBuilder.DropTable(
                name: "RebateRealizations");

            migrationBuilder.DropTable(
                name: "RebateRules");

            migrationBuilder.DropColumn(
                name: "RebateWithholdingRate",
                table: "AppSettings");
        }
    }
}
