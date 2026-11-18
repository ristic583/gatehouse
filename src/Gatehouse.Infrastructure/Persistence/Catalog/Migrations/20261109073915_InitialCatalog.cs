using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gatehouse.Infrastructure.Persistence.Catalog.Migrations;

/// <inheritdoc />
public partial class InitialCatalog : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "catalog");

        migrationBuilder.CreateTable(
            name: "tenants",
            schema: "catalog",
            columns: table => new
            {
                id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                display_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                database_name = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                encrypted_connection_string = table.Column<string>(type: "text", nullable: false),
                idp_kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                keycloak_organization_alias = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_tenants", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "ix_tenants_database_name",
            schema: "catalog",
            table: "tenants",
            column: "database_name",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "tenants",
            schema: "catalog");
    }
}
