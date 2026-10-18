using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SimpleERP.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class CommissionClawbackReturnLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CustomerReturnId",
                table: "CommissionAccruals",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommissionAccruals_CustomerReturnId",
                table: "CommissionAccruals",
                column: "CustomerReturnId");

            migrationBuilder.AddForeignKey(
                name: "FK_CommissionAccruals_CustomerReturns_CustomerReturnId",
                table: "CommissionAccruals",
                column: "CustomerReturnId",
                principalTable: "CustomerReturns",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CommissionAccruals_CustomerReturns_CustomerReturnId",
                table: "CommissionAccruals");

            migrationBuilder.DropIndex(
                name: "IX_CommissionAccruals_CustomerReturnId",
                table: "CommissionAccruals");

            migrationBuilder.DropColumn(
                name: "CustomerReturnId",
                table: "CommissionAccruals");
        }
    }
}
