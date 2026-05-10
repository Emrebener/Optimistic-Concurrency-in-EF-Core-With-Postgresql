using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OptimisticConcurrencyDemo.Migrations
{
    /// <inheritdoc />
    public partial class SwitchToXminConcurrencyToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The Version property is now mapped to PostgreSQL's xmin system column
            // via the Npgsql convention (uint property with IsRowVersion()). xmin
            // already exists on every table, so we just drop the user-defined column.
            migrationBuilder.DropColumn(
                name: "Version",
                table: "Coupons");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "Version",
                table: "Coupons",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));
        }
    }
}
