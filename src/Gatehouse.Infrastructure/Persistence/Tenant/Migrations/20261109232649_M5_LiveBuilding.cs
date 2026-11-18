using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gatehouse.Infrastructure.Persistence.Tenant.Migrations
{
    /// <inheritdoc />
    public partial class M5_LiveBuilding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "offline_notified_at",
                table: "pacs_instances",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "alarms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    door_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    raised_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    door_event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    acknowledged_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    acknowledged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    comment = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_alarms", x => x.id);
                    table.ForeignKey(
                        name: "fk_alarms_doors_door_id",
                        column: x => x.door_id,
                        principalTable: "doors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "door_commands",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    scope = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    scope_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    duration_seconds = table.Column<int>(type: "integer", nullable: true),
                    door_count = table.Column<int>(type: "integer", nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_door_commands", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_alarms_door_id_kind",
                table: "alarms",
                columns: new[] { "door_id", "kind" },
                unique: true,
                filter: "acknowledged_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_alarms_raised_at",
                table: "alarms",
                column: "raised_at");

            migrationBuilder.CreateIndex(
                name: "ix_door_commands_requested_at",
                table: "door_commands",
                column: "requested_at");

            // door_events is range-partitioned by day (ADR-006); EF does not generate partitioned tables.
            migrationBuilder.Sql("""
                CREATE TABLE door_events (
                    occurred_at timestamp with time zone NOT NULL,
                    id uuid NOT NULL,
                    received_at timestamp with time zone NOT NULL,
                    pacs_instance_id uuid NOT NULL,
                    door_id uuid NULL,
                    reader_id uuid NULL,
                    pacs_door_ref character varying(100) NOT NULL,
                    pacs_reader_ref character varying(100) NULL,
                    type character varying(30) NOT NULL,
                    person_id uuid NULL,
                    card_number_hash character varying(64) NULL,
                    direction character varying(10) NULL,
                    CONSTRAINT pk_door_events PRIMARY KEY (occurred_at, id)
                ) PARTITION BY RANGE (occurred_at);
                CREATE INDEX ix_door_events_door_id_occurred_at ON door_events (door_id, occurred_at DESC);
                CREATE INDEX ix_door_events_person_id_occurred_at ON door_events (person_id, occurred_at DESC);
                CREATE TABLE door_events_default PARTITION OF door_events DEFAULT;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS door_events;");

            migrationBuilder.DropTable(
                name: "alarms");

            migrationBuilder.DropTable(
                name: "door_commands");

            migrationBuilder.DropColumn(
                name: "offline_notified_at",
                table: "pacs_instances");
        }
    }
}
