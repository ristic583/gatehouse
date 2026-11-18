using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gatehouse.Infrastructure.Persistence.Tenant.Migrations
{
    /// <inheritdoc />
    public partial class M2_SitesAndPeople : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "persons",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    given_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    family_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    identity_subject = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    manager_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sponsor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    eligibility_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    eligibility_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    eligibility_time_zone_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_persons", x => x.id);
                    table.ForeignKey(
                        name: "fk_persons_persons_manager_id",
                        column: x => x.manager_id,
                        principalTable: "persons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_persons_persons_sponsor_id",
                        column: x => x.sponsor_id,
                        principalTable: "persons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "security_levels",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    rank = table.Column<int>(type: "integer", nullable: false),
                    approver_chain = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    max_duration_days = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_security_levels", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sites",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    time_zone_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sites", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "floors",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    level = table.Column<int>(type: "integer", nullable: false),
                    plan_width = table.Column<int>(type: "integer", nullable: false),
                    plan_height = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_floors", x => x.id);
                    table.ForeignKey(
                        name: "fk_floors_sites_site_id",
                        column: x => x.site_id,
                        principalTable: "sites",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "role_assignments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_role_assignments", x => x.id);
                    table.ForeignKey(
                        name: "fk_role_assignments_persons_person_id",
                        column: x => x.person_id,
                        principalTable: "persons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_role_assignments_sites_site_id",
                        column: x => x.site_id,
                        principalTable: "sites",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "areas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    floor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    polygon = table.Column<string>(type: "jsonb", nullable: false),
                    security_level_id = table.Column<Guid>(type: "uuid", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_areas", x => x.id);
                    table.ForeignKey(
                        name: "fk_areas_floors_floor_id",
                        column: x => x.floor_id,
                        principalTable: "floors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_areas_security_levels_security_level_id",
                        column: x => x.security_level_id,
                        principalTable: "security_levels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "area_owners",
                columns: table => new
                {
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    area_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_area_owners", x => new { x.area_id, x.person_id });
                    table.ForeignKey(
                        name: "fk_area_owners_areas_area_id",
                        column: x => x.area_id,
                        principalTable: "areas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_area_owners_persons_person_id",
                        column: x => x.person_id,
                        principalTable: "persons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "doors",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    area_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    pacs_door_ref = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    position_x = table.Column<double>(type: "double precision", nullable: false),
                    position_y = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_doors", x => x.id);
                    table.ForeignKey(
                        name: "fk_doors_areas_area_id",
                        column: x => x.area_id,
                        principalTable: "areas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "readers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    door_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    direction = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    pacs_reader_ref = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    position_x = table.Column<double>(type: "double precision", nullable: false),
                    position_y = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_readers", x => x.id);
                    table.ForeignKey(
                        name: "fk_readers_doors_door_id",
                        column: x => x.door_id,
                        principalTable: "doors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_area_owners_person_id",
                table: "area_owners",
                column: "person_id");

            migrationBuilder.CreateIndex(
                name: "ix_areas_code",
                table: "areas",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_areas_floor_id_name",
                table: "areas",
                columns: new[] { "floor_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_areas_security_level_id",
                table: "areas",
                column: "security_level_id");

            migrationBuilder.CreateIndex(
                name: "ix_doors_area_id",
                table: "doors",
                column: "area_id");

            migrationBuilder.CreateIndex(
                name: "ix_floors_site_id_level",
                table: "floors",
                columns: new[] { "site_id", "level" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_persons_email",
                table: "persons",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_persons_identity_subject",
                table: "persons",
                column: "identity_subject",
                unique: true,
                filter: "identity_subject IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_persons_manager_id",
                table: "persons",
                column: "manager_id");

            migrationBuilder.CreateIndex(
                name: "ix_persons_sponsor_id",
                table: "persons",
                column: "sponsor_id");

            migrationBuilder.CreateIndex(
                name: "ix_readers_door_id",
                table: "readers",
                column: "door_id");

            migrationBuilder.CreateIndex(
                name: "ix_role_assignments_person_id_role_site_id",
                table: "role_assignments",
                columns: new[] { "person_id", "role", "site_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_role_assignments_site_id",
                table: "role_assignments",
                column: "site_id");

            migrationBuilder.CreateIndex(
                name: "ix_security_levels_name",
                table: "security_levels",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_security_levels_rank",
                table: "security_levels",
                column: "rank",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sites_code",
                table: "sites",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sites_name",
                table: "sites",
                column: "name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "area_owners");

            migrationBuilder.DropTable(
                name: "readers");

            migrationBuilder.DropTable(
                name: "role_assignments");

            migrationBuilder.DropTable(
                name: "doors");

            migrationBuilder.DropTable(
                name: "persons");

            migrationBuilder.DropTable(
                name: "areas");

            migrationBuilder.DropTable(
                name: "floors");

            migrationBuilder.DropTable(
                name: "security_levels");

            migrationBuilder.DropTable(
                name: "sites");
        }
    }
}
