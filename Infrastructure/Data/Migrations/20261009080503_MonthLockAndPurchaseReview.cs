using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SimpleERP.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class MonthLockAndPurchaseReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "EnteredWithoutPrice",
                table: "Purchases",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastStaffEditAt",
                table: "Purchases",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastStaffEditBy",
                table: "Purchases",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "NeedsReview",
                table: "Purchases",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReviewedAt",
                table: "Purchases",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewedBy",
                table: "Purchases",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "BooksClosedThrough",
                table: "AppSettings",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "AppSettings",
                keyColumn: "Id",
                keyValue: "default",
                column: "BooksClosedThrough",
                value: null);

            migrationBuilder.CreateIndex(
                name: "IX_Purchases_NeedsReview",
                table: "Purchases",
                column: "NeedsReview");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Purchases_NeedsReview",
                table: "Purchases");

            migrationBuilder.DropColumn(
                name: "EnteredWithoutPrice",
                table: "Purchases");

            migrationBuilder.DropColumn(
                name: "LastStaffEditAt",
                table: "Purchases");

            migrationBuilder.DropColumn(
                name: "LastStaffEditBy",
                table: "Purchases");

            migrationBuilder.DropColumn(
                name: "NeedsReview",
                table: "Purchases");

            migrationBuilder.DropColumn(
                name: "ReviewedAt",
                table: "Purchases");

            migrationBuilder.DropColumn(
                name: "ReviewedBy",
                table: "Purchases");

            migrationBuilder.DropColumn(
                name: "BooksClosedThrough",
                table: "AppSettings");
        }
    }
}
