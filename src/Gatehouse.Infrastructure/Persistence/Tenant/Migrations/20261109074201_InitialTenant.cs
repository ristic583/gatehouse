using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gatehouse.Infrastructure.Persistence.Tenant.Migrations;

/// <inheritdoc />
public partial class InitialTenant : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "tenant_settings",
            columns: table => new
            {
                key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                value = table.Column<string>(type: "text", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_tenant_settings", x => x.key);
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "tenant_settings");
    }
}
