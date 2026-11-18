using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gatehouse.Infrastructure.Persistence.Tenant.Migrations
{
    /// <inheritdoc />
    public partial class M10_FloorBackground : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "background_content_type",
                table: "floors",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "background_height_px",
                table: "floors",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "background_object_key",
                table: "floors",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "background_updated_at",
                table: "floors",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "background_width_px",
                table: "floors",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "background_content_type",
                table: "floors");

            migrationBuilder.DropColumn(
                name: "background_height_px",
                table: "floors");

            migrationBuilder.DropColumn(
                name: "background_object_key",
                table: "floors");

            migrationBuilder.DropColumn(
                name: "background_updated_at",
                table: "floors");

            migrationBuilder.DropColumn(
                name: "background_width_px",
                table: "floors");
        }
    }
}
