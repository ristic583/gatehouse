using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gatehouse.Infrastructure.Persistence.Tenant.Migrations
{
    /// <inheritdoc />
    public partial class PendingRequestUnique : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ux_entitlements_pending_person_area",
                table: "entitlements",
                columns: new[] { "person_id", "area_id" },
                unique: true,
                filter: "status = 'PendingApproval'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_entitlements_pending_person_area",
                table: "entitlements");
        }
    }
}
