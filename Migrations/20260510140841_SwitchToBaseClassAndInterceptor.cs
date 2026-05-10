using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OptimisticConcurrencyDemo.Migrations
{
    /// <inheritdoc />
    public partial class SwitchToBaseClassAndInterceptor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Coupons.Version is a real uuid column again; the previous migration's
            // mapping to xmin (a system column with no physical user column) is gone.
            migrationBuilder.AddColumn<Guid>(
                name: "Version",
                table: "Coupons",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "Promotions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    StartsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Promotions", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Promotions");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "Coupons");
        }
    }
}
