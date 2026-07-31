using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SimpleERP.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentBatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PaymentBatchId",
                table: "SupplierPayments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PaymentBatchId",
                table: "PaymentRecords",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SettledByPaymentBatchId",
                table: "CreditNotes",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PaymentBatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Direction = table.Column<int>(type: "integer", nullable: false),
                    BatchDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: true),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: true),
                    GrossAmount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    NotesAppliedAmount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    NetAmount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentBatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentBatches_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaymentBatches_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SupplierPayments_PaymentBatchId",
                table: "SupplierPayments",
                column: "PaymentBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRecords_PaymentBatchId",
                table: "PaymentRecords",
                column: "PaymentBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotes_SettledByPaymentBatchId",
                table: "CreditNotes",
                column: "SettledByPaymentBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentBatches_BatchNumber",
                table: "PaymentBatches",
                column: "BatchNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentBatches_CustomerId",
                table: "PaymentBatches",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentBatches_Direction_BatchDate",
                table: "PaymentBatches",
                columns: new[] { "Direction", "BatchDate" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentBatches_SupplierId",
                table: "PaymentBatches",
                column: "SupplierId");

            migrationBuilder.AddForeignKey(
                name: "FK_CreditNotes_PaymentBatches_SettledByPaymentBatchId",
                table: "CreditNotes",
                column: "SettledByPaymentBatchId",
                principalTable: "PaymentBatches",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_PaymentRecords_PaymentBatches_PaymentBatchId",
                table: "PaymentRecords",
                column: "PaymentBatchId",
                principalTable: "PaymentBatches",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_SupplierPayments_PaymentBatches_PaymentBatchId",
                table: "SupplierPayments",
                column: "PaymentBatchId",
                principalTable: "PaymentBatches",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CreditNotes_PaymentBatches_SettledByPaymentBatchId",
                table: "CreditNotes");

            migrationBuilder.DropForeignKey(
                name: "FK_PaymentRecords_PaymentBatches_PaymentBatchId",
                table: "PaymentRecords");

            migrationBuilder.DropForeignKey(
                name: "FK_SupplierPayments_PaymentBatches_PaymentBatchId",
                table: "SupplierPayments");

            migrationBuilder.DropTable(
                name: "PaymentBatches");

            migrationBuilder.DropIndex(
                name: "IX_SupplierPayments_PaymentBatchId",
                table: "SupplierPayments");

            migrationBuilder.DropIndex(
                name: "IX_PaymentRecords_PaymentBatchId",
                table: "PaymentRecords");

            migrationBuilder.DropIndex(
                name: "IX_CreditNotes_SettledByPaymentBatchId",
                table: "CreditNotes");

            migrationBuilder.DropColumn(
                name: "PaymentBatchId",
                table: "SupplierPayments");

            migrationBuilder.DropColumn(
                name: "PaymentBatchId",
                table: "PaymentRecords");

            migrationBuilder.DropColumn(
                name: "SettledByPaymentBatchId",
                table: "CreditNotes");
        }
    }
}
