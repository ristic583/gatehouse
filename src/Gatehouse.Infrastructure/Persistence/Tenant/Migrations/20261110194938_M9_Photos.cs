using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gatehouse.Infrastructure.Persistence.Tenant.Migrations
{
    /// <inheritdoc />
    public partial class M9_Photos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "photos",
                columns: table => new
                {
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    object_key = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    content_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    uploaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_photos", x => x.person_id);
                    table.ForeignKey(
                        name: "fk_photos_persons_person_id",
                        column: x => x.person_id,
                        principalTable: "persons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "photos");
        }
    }
}
