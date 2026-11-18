using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gatehouse.Infrastructure.Persistence.Tenant.Migrations
{
    /// <inheritdoc />
    public partial class M7_Reconciliation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "reconciliation_reports",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pacs_instance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    command_id = table.Column<Guid>(type: "uuid", nullable: false),
                    received_chunk_count = table.Column<int>(type: "integer", nullable: false),
                    captured_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    compared_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    coverage = table.Column<string>(type: "jsonb", nullable: true),
                    failure_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    failure_message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reconciliation_reports", x => x.id);
                    table.ForeignKey(
                        name: "fk_reconciliation_reports_pacs_instances_pacs_instance_id",
                        column: x => x.pacs_instance_id,
                        principalTable: "pacs_instances",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "reconciliation_export_records",
                columns: table => new
                {
                    report_id = table.Column<Guid>(type: "uuid", nullable: false),
                    chunk_index = table.Column<int>(type: "integer", nullable: false),
                    ordinal = table.Column<int>(type: "integer", nullable: false),
                    record_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    pacs_ref = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    person_ref = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    card_number = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    vendor_access_level_ref = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    valid_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    valid_to = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reconciliation_export_records", x => new { x.report_id, x.chunk_index, x.ordinal });
                    table.ForeignKey(
                        name: "fk_reconciliation_export_records_reconciliation_reports_report",
                        column: x => x.report_id,
                        principalTable: "reconciliation_reports",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "reconciliation_findings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    report_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: true),
                    area_id = table.Column<Guid>(type: "uuid", nullable: true),
                    entitlement_id = table.Column<Guid>(type: "uuid", nullable: true),
                    fulfillment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    pacs_ref = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    person_ref = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    vendor_access_level_ref = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    pacs_card_number = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    details = table.Column<string>(type: "jsonb", nullable: false),
                    staff_task_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reconciliation_findings", x => x.id);
                    table.ForeignKey(
                        name: "fk_reconciliation_findings_reconciliation_reports_report_id",
                        column: x => x.report_id,
                        principalTable: "reconciliation_reports",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_reconciliation_findings_report_id",
                table: "reconciliation_findings",
                column: "report_id");

            migrationBuilder.CreateIndex(
                name: "ix_reconciliation_findings_staff_task_id",
                table: "reconciliation_findings",
                column: "staff_task_id");

            migrationBuilder.CreateIndex(
                name: "ix_reconciliation_reports_pacs_instance_id_requested_at",
                table: "reconciliation_reports",
                columns: new[] { "pacs_instance_id", "requested_at" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "reconciliation_export_records");

            migrationBuilder.DropTable(
                name: "reconciliation_findings");

            migrationBuilder.DropTable(
                name: "reconciliation_reports");
        }
    }
}
