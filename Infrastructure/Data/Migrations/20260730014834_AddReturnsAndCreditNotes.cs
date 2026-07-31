using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SimpleERP.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReturnsAndCreditNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CustomerReturns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReturnNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ReturnDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    SaleId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubTotal = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    TaxBase = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    TaxRate = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    TaxAmount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    IsTaxInclusive = table.Column<bool>(type: "boolean", nullable: false),
                    GrandTotal = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerReturns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerReturns_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerReturns_Sales_SaleId",
                        column: x => x.SaleId,
                        principalTable: "Sales",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SupplierReturns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReturnNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ReturnDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    PurchaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubTotal = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    TaxBase = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    TaxRate = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    TaxAmount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    IsTaxInclusive = table.Column<bool>(type: "boolean", nullable: false),
                    GrandTotal = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierReturns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupplierReturns_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierReturns_Purchases_PurchaseId",
                        column: x => x.PurchaseId,
                        principalTable: "Purchases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CustomerReturnItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerReturnId = table.Column<Guid>(type: "uuid", nullable: false),
                    SaleItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    Qty = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    CreditAmount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    CostAtSale = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerReturnItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerReturnItems_CustomerReturns_CustomerReturnId",
                        column: x => x.CustomerReturnId,
                        principalTable: "CustomerReturns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CustomerReturnItems_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerReturnItems_SaleItems_SaleItemId",
                        column: x => x.SaleItemId,
                        principalTable: "SaleItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CreditNotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Category = table.Column<int>(type: "integer", nullable: false),
                    NoteDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: true),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: true),
                    TaxBase = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    TaxRate = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    TaxAmount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    SourceSaleId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourcePurchaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceCustomerReturnId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceSupplierReturnId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    SettledDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    SettlementNotes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreditNotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CreditNotes_CustomerReturns_SourceCustomerReturnId",
                        column: x => x.SourceCustomerReturnId,
                        principalTable: "CustomerReturns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CreditNotes_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CreditNotes_Purchases_SourcePurchaseId",
                        column: x => x.SourcePurchaseId,
                        principalTable: "Purchases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CreditNotes_Sales_SourceSaleId",
                        column: x => x.SourceSaleId,
                        principalTable: "Sales",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CreditNotes_SupplierReturns_SourceSupplierReturnId",
                        column: x => x.SourceSupplierReturnId,
                        principalTable: "SupplierReturns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CreditNotes_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SupplierReturnItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierReturnId = table.Column<Guid>(type: "uuid", nullable: false),
                    PurchaseItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    Qty = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    UnitCost = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    DebitAmount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    CostAtReturn = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierReturnItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupplierReturnItems_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierReturnItems_PurchaseItems_PurchaseItemId",
                        column: x => x.PurchaseItemId,
                        principalTable: "PurchaseItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierReturnItems_SupplierReturns_SupplierReturnId",
                        column: x => x.SupplierReturnId,
                        principalTable: "SupplierReturns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotes_CustomerId",
                table: "CreditNotes",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotes_DocumentNumber",
                table: "CreditNotes",
                column: "DocumentNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotes_NoteDate",
                table: "CreditNotes",
                column: "NoteDate");

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotes_SourceCustomerReturnId",
                table: "CreditNotes",
                column: "SourceCustomerReturnId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotes_SourcePurchaseId",
                table: "CreditNotes",
                column: "SourcePurchaseId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotes_SourceSaleId",
                table: "CreditNotes",
                column: "SourceSaleId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotes_SourceSupplierReturnId",
                table: "CreditNotes",
                column: "SourceSupplierReturnId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotes_SupplierId",
                table: "CreditNotes",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotes_Type_Status",
                table: "CreditNotes",
                columns: new[] { "Type", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReturnItems_CustomerReturnId",
                table: "CustomerReturnItems",
                column: "CustomerReturnId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReturnItems_ProductId",
                table: "CustomerReturnItems",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReturnItems_SaleItemId",
                table: "CustomerReturnItems",
                column: "SaleItemId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReturns_BranchId",
                table: "CustomerReturns",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReturns_ReturnDate",
                table: "CustomerReturns",
                column: "ReturnDate");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReturns_ReturnNumber",
                table: "CustomerReturns",
                column: "ReturnNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReturns_SaleId",
                table: "CustomerReturns",
                column: "SaleId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierReturnItems_ProductId",
                table: "SupplierReturnItems",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierReturnItems_PurchaseItemId",
                table: "SupplierReturnItems",
                column: "PurchaseItemId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierReturnItems_SupplierReturnId",
                table: "SupplierReturnItems",
                column: "SupplierReturnId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierReturns_BranchId",
                table: "SupplierReturns",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierReturns_PurchaseId",
                table: "SupplierReturns",
                column: "PurchaseId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierReturns_ReturnDate",
                table: "SupplierReturns",
                column: "ReturnDate");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierReturns_ReturnNumber",
                table: "SupplierReturns",
                column: "ReturnNumber",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CreditNotes");

            migrationBuilder.DropTable(
                name: "CustomerReturnItems");

            migrationBuilder.DropTable(
                name: "SupplierReturnItems");

            migrationBuilder.DropTable(
                name: "CustomerReturns");

            migrationBuilder.DropTable(
                name: "SupplierReturns");
        }
    }
}
