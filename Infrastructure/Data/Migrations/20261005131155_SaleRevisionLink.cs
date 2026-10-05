using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SimpleERP.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class SaleRevisionLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ReplacesSaleId",
                table: "Sales",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sales_ReplacesSaleId",
                table: "Sales",
                column: "ReplacesSaleId");

            migrationBuilder.AddForeignKey(
                name: "FK_Sales_Sales_ReplacesSaleId",
                table: "Sales",
                column: "ReplacesSaleId",
                principalTable: "Sales",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Sales_Sales_ReplacesSaleId",
                table: "Sales");

            migrationBuilder.DropIndex(
                name: "IX_Sales_ReplacesSaleId",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "ReplacesSaleId",
                table: "Sales");
        }
    }
}
