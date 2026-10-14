using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SimpleERP.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class BusinessRulesG1toG9 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CancelReason",
                table: "Sales",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CancelledAt",
                table: "Sales",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CancelledBy",
                table: "Sales",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "NeedsReview",
                table: "Sales",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ReviewReasons",
                table: "Sales",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReviewedAt",
                table: "Sales",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewedBy",
                table: "Sales",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SubmissionId",
                table: "Sales",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "NeedsReview",
                table: "CustomerReturns",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReviewedAt",
                table: "CustomerReturns",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewedBy",
                table: "CustomerReturns",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ApprovedAt",
                table: "CreditNotes",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovedBy",
                table: "CreditNotes",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "NeedsApproval",
                table: "CreditNotes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "StaffDiscountLimitPercent",
                table: "AppSettings",
                type: "numeric(9,4)",
                nullable: false,
                defaultValue: 10m);   // the Staff discount limit starts at 10% (HC, 2026-10-14)

            migrationBuilder.UpdateData(
                table: "AppSettings",
                keyColumn: "Id",
                keyValue: "default",
                column: "StaffDiscountLimitPercent",
                value: 10m);

            migrationBuilder.CreateIndex(
                name: "IX_Sales_CancelledAt",
                table: "Sales",
                column: "CancelledAt");

            migrationBuilder.CreateIndex(
                name: "IX_Sales_NeedsReview",
                table: "Sales",
                column: "NeedsReview");

            migrationBuilder.CreateIndex(
                name: "IX_Sales_SubmissionId",
                table: "Sales",
                column: "SubmissionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReturns_NeedsReview",
                table: "CustomerReturns",
                column: "NeedsReview");

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotes_NeedsApproval",
                table: "CreditNotes",
                column: "NeedsApproval");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Sales_CancelledAt",
                table: "Sales");

            migrationBuilder.DropIndex(
                name: "IX_Sales_NeedsReview",
                table: "Sales");

            migrationBuilder.DropIndex(
                name: "IX_Sales_SubmissionId",
                table: "Sales");

            migrationBuilder.DropIndex(
                name: "IX_CustomerReturns_NeedsReview",
                table: "CustomerReturns");

            migrationBuilder.DropIndex(
                name: "IX_CreditNotes_NeedsApproval",
                table: "CreditNotes");

            migrationBuilder.DropColumn(
                name: "CancelReason",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "CancelledAt",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "CancelledBy",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "NeedsReview",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "ReviewReasons",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "ReviewedAt",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "ReviewedBy",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "SubmissionId",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "NeedsReview",
                table: "CustomerReturns");

            migrationBuilder.DropColumn(
                name: "ReviewedAt",
                table: "CustomerReturns");

            migrationBuilder.DropColumn(
                name: "ReviewedBy",
                table: "CustomerReturns");

            migrationBuilder.DropColumn(
                name: "ApprovedAt",
                table: "CreditNotes");

            migrationBuilder.DropColumn(
                name: "ApprovedBy",
                table: "CreditNotes");

            migrationBuilder.DropColumn(
                name: "NeedsApproval",
                table: "CreditNotes");

            migrationBuilder.DropColumn(
                name: "StaffDiscountLimitPercent",
                table: "AppSettings");
        }
    }
}
