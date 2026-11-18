using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gatehouse.Infrastructure.Persistence.Tenant.Migrations
{
    /// <inheritdoc />
    public partial class M8_Lifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_cards_card_number",
                table: "cards");

            migrationBuilder.DropIndex(
                name: "ix_cards_person_id",
                table: "cards");

            // M8: at most one non-revoked card per person; revoke older duplicates (keep the newest) before the filtered unique index.
            migrationBuilder.Sql("""
                UPDATE cards SET state = 'Revoked'
                WHERE id IN (
                    SELECT id FROM (
                        SELECT id, row_number() OVER (PARTITION BY person_id ORDER BY state_changed_at DESC, id DESC) AS rn
                        FROM cards WHERE state <> 'Revoked') ranked
                    WHERE rn > 1);
                """);

            migrationBuilder.CreateTable(
                name: "magic_links",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_magic_links", x => x.id);
                    table.ForeignKey(
                        name: "fk_magic_links_persons_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "persons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_magic_links_persons_person_id",
                        column: x => x.person_id,
                        principalTable: "persons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "pin_credentials",
                columns: table => new
                {
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ciphertext = table.Column<byte[]>(type: "bytea", nullable: false),
                    key_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    set_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pin_credentials", x => x.person_id);
                    table.ForeignKey(
                        name: "fk_pin_credentials_persons_person_id",
                        column: x => x.person_id,
                        principalTable: "persons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cards_card_number",
                table: "cards",
                column: "card_number",
                unique: true,
                filter: "state <> 'Revoked'");

            migrationBuilder.CreateIndex(
                name: "ix_cards_person_id",
                table: "cards",
                column: "person_id",
                unique: true,
                filter: "state <> 'Revoked'");

            migrationBuilder.CreateIndex(
                name: "ix_magic_links_created_by_id",
                table: "magic_links",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_magic_links_person_id_created_at",
                table: "magic_links",
                columns: new[] { "person_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_magic_links_token_hash",
                table: "magic_links",
                column: "token_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "magic_links");

            migrationBuilder.DropTable(
                name: "pin_credentials");

            migrationBuilder.DropIndex(
                name: "ix_cards_card_number",
                table: "cards");

            migrationBuilder.DropIndex(
                name: "ix_cards_person_id",
                table: "cards");

            migrationBuilder.CreateIndex(
                name: "ix_cards_card_number",
                table: "cards",
                column: "card_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_cards_person_id",
                table: "cards",
                column: "person_id");
        }
    }
}
