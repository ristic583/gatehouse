using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gatehouse.Infrastructure.Persistence.Tenant.Migrations
{
    /// <inheritdoc />
    public partial class M4_ConnectorsAndCards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "fulfillment_mode",
                table: "pacs_instances",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Manual");

            migrationBuilder.CreateTable(
                name: "cards",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    card_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    state_changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cards", x => x.id);
                    table.ForeignKey(
                        name: "fk_cards_persons_person_id",
                        column: x => x.person_id,
                        principalTable: "persons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "connector_commands",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pacs_instance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    partition_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    sequence = table.Column<long>(type: "bigint", nullable: false),
                    priority = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    lease_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    delivered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    acked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    pacs_ref = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    fulfillment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_connector_commands", x => x.id);
                    table.ForeignKey(
                        name: "fk_connector_commands_pacs_instances_pacs_instance_id",
                        column: x => x.pacs_instance_id,
                        principalTable: "pacs_instances",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "connector_partition_sequences",
                columns: table => new
                {
                    pacs_instance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    partition_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    last_sequence = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_connector_partition_sequences", x => new { x.pacs_instance_id, x.partition_key });
                });

            migrationBuilder.CreateIndex(
                name: "ix_cards_card_number",
                table: "cards",
                column: "card_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_cards_person_id",
                table: "cards",
                column: "person_id");

            migrationBuilder.CreateIndex(
                name: "ix_connector_commands_fulfillment_id",
                table: "connector_commands",
                column: "fulfillment_id");

            migrationBuilder.CreateIndex(
                name: "ix_connector_commands_idempotency_key",
                table: "connector_commands",
                column: "idempotency_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_connector_commands_pacs_instance_id_partition_key_sequence",
                table: "connector_commands",
                columns: new[] { "pacs_instance_id", "partition_key", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_connector_commands_pacs_instance_id_status_next_attempt_at",
                table: "connector_commands",
                columns: new[] { "pacs_instance_id", "status", "next_attempt_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cards");

            migrationBuilder.DropTable(
                name: "connector_commands");

            migrationBuilder.DropTable(
                name: "connector_partition_sequences");

            migrationBuilder.DropColumn(
                name: "fulfillment_mode",
                table: "pacs_instances");
        }
    }
}
