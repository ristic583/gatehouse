using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gatehouse.Infrastructure.Persistence.Tenant.Migrations
{
    /// <inheritdoc />
    public partial class M3_TasksAndAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_entries",
                columns: table => new
                {
                    sequence = table.Column<long>(type: "bigint", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actor_kind = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    actor_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    subject_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    subject_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    prev_hash = table.Column<string>(type: "char(64)", nullable: false),
                    hash = table.Column<string>(type: "char(64)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_entries", x => x.sequence);
                });

            migrationBuilder.CreateTable(
                name: "staff_tasks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: true),
                    subject_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assigned_to_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    resolution_action = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    resolution_note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    resolved_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_staff_tasks", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_entries_subject_type_subject_id",
                table: "audit_entries",
                columns: new[] { "subject_type", "subject_id" });

            migrationBuilder.CreateIndex(
                name: "ix_staff_tasks_kind_subject_id",
                table: "staff_tasks",
                columns: new[] { "kind", "subject_id" },
                unique: true,
                filter: "status <> 'Resolved'");

            migrationBuilder.CreateIndex(
                name: "ix_staff_tasks_status_site_id",
                table: "staff_tasks",
                columns: new[] { "status", "site_id" });

            migrationBuilder.Sql(
                """
                CREATE FUNCTION audit_entries_block_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'audit_entries is append-only'; END $$;
                CREATE TRIGGER audit_entries_no_update_delete BEFORE UPDATE OR DELETE ON audit_entries
                  FOR EACH ROW EXECUTE FUNCTION audit_entries_block_mutation();
                CREATE TRIGGER audit_entries_no_truncate BEFORE TRUNCATE ON audit_entries
                  FOR EACH STATEMENT EXECUTE FUNCTION audit_entries_block_mutation();
                REVOKE UPDATE, DELETE, TRUNCATE ON audit_entries FROM PUBLIC;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TRIGGER audit_entries_no_truncate ON audit_entries;
                DROP TRIGGER audit_entries_no_update_delete ON audit_entries;
                DROP FUNCTION audit_entries_block_mutation();
                """);

            migrationBuilder.DropTable(
                name: "audit_entries");

            migrationBuilder.DropTable(
                name: "staff_tasks");
        }
    }
}
