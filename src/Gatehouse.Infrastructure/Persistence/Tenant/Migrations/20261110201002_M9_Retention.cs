using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gatehouse.Infrastructure.Persistence.Tenant.Migrations
{
    /// <inheritdoc />
    public partial class M9_Retention : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "anonymized_at",
                table: "persons",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "terminated_at",
                table: "persons",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql("UPDATE persons SET terminated_at = COALESCE(eligibility_end, now()) WHERE status = 'Terminated' AND terminated_at IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "anonymized_at",
                table: "persons");

            migrationBuilder.DropColumn(
                name: "terminated_at",
                table: "persons");
        }
    }
}
