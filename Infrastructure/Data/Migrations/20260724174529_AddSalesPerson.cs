using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SimpleERP.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesPerson : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SalesPersonId",
                table: "Sales",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SalesPersons",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesPersons", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Sales_SalesPersonId",
                table: "Sales",
                column: "SalesPersonId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesPersons_Name",
                table: "SalesPersons",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Sales_SalesPersons_SalesPersonId",
                table: "Sales",
                column: "SalesPersonId",
                principalTable: "SalesPersons",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Sales_SalesPersons_SalesPersonId",
                table: "Sales");

            migrationBuilder.DropTable(
                name: "SalesPersons");

            migrationBuilder.DropIndex(
                name: "IX_Sales_SalesPersonId",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "SalesPersonId",
                table: "Sales");
        }
    }
}
