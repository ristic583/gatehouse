using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gatehouse.Infrastructure.Persistence.Tenant.Migrations
{
    /// <inheritdoc />
    public partial class M2_AccessAndOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "entitlements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    area_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    requested_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    effective_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    capped_to_eligibility = table.Column<bool>(type: "boolean", nullable: false),
                    justification = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revocation_revoked_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    revocation_revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revocation_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    revocation_kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_entitlements", x => x.id);
                    table.ForeignKey(
                        name: "fk_entitlements_areas_area_id",
                        column: x => x.area_id,
                        principalTable: "areas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_entitlements_persons_person_id",
                        column: x => x.person_id,
                        principalTable: "persons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_entitlements_persons_requested_by_id",
                        column: x => x.requested_by_id,
                        principalTable: "persons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    priority = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    dispatched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "pacs_instances",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    vendor = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    site_ids = table.Column<string>(type: "jsonb", nullable: false),
                    capabilities = table.Column<string>(type: "jsonb", nullable: true),
                    last_heartbeat_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    agent_version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pacs_instances", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "processed_messages",
                columns: table => new
                {
                    message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    consumer = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_processed_messages", x => new { x.message_id, x.consumer });
                });

            migrationBuilder.CreateTable(
                name: "approval_steps",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order = table.Column<int>(type: "integer", nullable: false),
                    approver_kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    assigned_approver_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    decided_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    comment = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    entitlement_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_approval_steps", x => x.id);
                    table.ForeignKey(
                        name: "fk_approval_steps_entitlements_entitlement_id",
                        column: x => x.entitlement_id,
                        principalTable: "entitlements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "access_level_mappings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    area_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pacs_instance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vendor_access_level_ref = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_access_level_mappings", x => x.id);
                    table.ForeignKey(
                        name: "fk_access_level_mappings_areas_area_id",
                        column: x => x.area_id,
                        principalTable: "areas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_access_level_mappings_pacs_instances_pacs_instance_id",
                        column: x => x.pacs_instance_id,
                        principalTable: "pacs_instances",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "fulfillments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    entitlement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pacs_instance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    priority = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    command_id = table.Column<Guid>(type: "uuid", nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    last_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    pacs_ref = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    confirmed_by_kind = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    confirmed_by_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fulfillments", x => x.id);
                    table.ForeignKey(
                        name: "fk_fulfillments_entitlements_entitlement_id",
                        column: x => x.entitlement_id,
                        principalTable: "entitlements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_fulfillments_pacs_instances_pacs_instance_id",
                        column: x => x.pacs_instance_id,
                        principalTable: "pacs_instances",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_access_level_mappings_area_id_pacs_instance_id",
                table: "access_level_mappings",
                columns: new[] { "area_id", "pacs_instance_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_access_level_mappings_pacs_instance_id",
                table: "access_level_mappings",
                column: "pacs_instance_id");

            migrationBuilder.CreateIndex(
                name: "ix_approval_steps_entitlement_id",
                table: "approval_steps",
                column: "entitlement_id");

            migrationBuilder.CreateIndex(
                name: "ix_approval_steps_status_approver_kind",
                table: "approval_steps",
                columns: new[] { "status", "approver_kind" });

            migrationBuilder.CreateIndex(
                name: "ix_entitlements_area_id",
                table: "entitlements",
                column: "area_id");

            migrationBuilder.CreateIndex(
                name: "ix_entitlements_person_id_area_id_status",
                table: "entitlements",
                columns: new[] { "person_id", "area_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_entitlements_requested_by_id",
                table: "entitlements",
                column: "requested_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_fulfillments_entitlement_id_pacs_instance_id_kind",
                table: "fulfillments",
                columns: new[] { "entitlement_id", "pacs_instance_id", "kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_fulfillments_pacs_instance_id",
                table: "fulfillments",
                column: "pacs_instance_id");

            migrationBuilder.CreateIndex(
                name: "ix_fulfillments_status",
                table: "fulfillments",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_dispatched_at_priority_occurred_at",
                table: "outbox_messages",
                columns: new[] { "dispatched_at", "priority", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_pacs_instances_name",
                table: "pacs_instances",
                column: "name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "access_level_mappings");

            migrationBuilder.DropTable(
                name: "approval_steps");

            migrationBuilder.DropTable(
                name: "fulfillments");

            migrationBuilder.DropTable(
                name: "outbox_messages");

            migrationBuilder.DropTable(
                name: "processed_messages");

            migrationBuilder.DropTable(
                name: "entitlements");

            migrationBuilder.DropTable(
                name: "pacs_instances");
        }
    }
}
