using Gatehouse.Domain.Fulfillment;
using Gatehouse.Domain.People;

namespace Gatehouse.Infrastructure.Seeding;

public static class NordlysSeed
{
    public static SeedDefinition Definition { get; } = new(
        "Europe/Copenhagen",
        "nordlys.test",
        [
            new("HQ Copenhagen", "CPH-HQ", [new(0, "Ground floor"), new(1, "First floor"), new(2, "Second floor")]),
            new("Warehouse Aarhus", "AAR-WH", [new(0, "Warehouse floor")]),
        ],
        [
            new("CPH-LOBBY", "Lobby", "CPH-HQ", 0, "Low", 40, 40, 400, 560, "erik.moller"),
            new("CPH-OFFICE-0", "Office Ground", "CPH-HQ", 0, "Low", 420, 40, 960, 560, "erik.moller"),
            new("CPH-OFFICE-1", "Office First", "CPH-HQ", 1, "Low", 40, 40, 600, 560, "erik.moller"),
            new("CPH-LAB", "Lab", "CPH-HQ", 1, "Medium", 620, 40, 960, 560, "sofie.berg"),
            new("CPH-OFFICE-2", "Office Second", "CPH-HQ", 2, "Low", 40, 40, 520, 400, "erik.moller"),
            new("CPH-PLANT", "Plant Room", "CPH-HQ", 2, "Medium", 40, 420, 520, 560, "erik.moller"),
            new("CPH-SERVER", "Server Room", "CPH-HQ", 2, "High", 540, 40, 960, 300, "sofie.berg"),
            new("CPH-VAULT", "Vault", "CPH-HQ", 2, "Restricted", 540, 320, 960, 560, "sofie.berg"),
            new("AAR-DOCK", "Loading Dock", "AAR-WH", 0, "Medium", 40, 40, 600, 560, "erik.moller"),
            new("AAR-OFFICE", "Warehouse Office", "AAR-WH", 0, "Low", 620, 40, 960, 560, "erik.moller"),
        ],
        [
            new("HQ-Copenhagen", PacsVendor.LegacyPacs, "CPH-HQ"),
            new("Warehouse-Aarhus", PacsVendor.CloudPacs, "AAR-WH"),
        ],
        new HashSet<string>(StringComparer.Ordinal) { "CPH-PLANT" },
        [
            new("erik.moller", "Erik", "Møller"),
            new("mikkel.holm", "Mikkel", "Holm", Manager: "erik.moller"),
            new("sofie.berg", "Sofie", "Berg", Manager: "erik.moller"),
            new("jonas.nielsen", "Jonas", "Nielsen", Manager: "erik.moller", Role: AssignedRole.SecurityStaff),
            new("freja.dahl", "Freja", "Dahl", Manager: "erik.moller", Role: AssignedRole.TenantAdmin),
            new("anna.larsen", "Anna", "Larsen", Manager: "mikkel.holm"),
            new("oliver.kruse", "Oliver", "Kruse", PersonType.External, Sponsor: "mikkel.holm"),
        ],
        new(
            ["Lars", "Mette", "Henrik", "Camilla", "Rasmus", "Ida", "Soren", "Line", "Mads", "Emma"],
            ["Jensen", "Hansen", "Pedersen", "Andersen", "Christensen"],
            EvenManager: "mikkel.holm",
            OddManager: "sofie.berg",
            ExternalSponsor: "mikkel.holm",
            VisitorSponsor: "anna.larsen",
            GuestSponsor: "freja.dahl"),
        [
            new("anna.larsen", "CPH-LOBBY", SeedEntitlementKind.Confirmed, -30, 335),
            new("anna.larsen", "CPH-OFFICE-1", SeedEntitlementKind.Confirmed, -30, 335),
            new("anna.larsen", "CPH-OFFICE-2", SeedEntitlementKind.Confirmed, -30, 335),
            new("anna.larsen", "CPH-LAB", SeedEntitlementKind.PendingAreaOwner, 1, 60),
            new("anna.larsen", "CPH-VAULT", SeedEntitlementKind.PendingManager, 1, 20),
        ]);
}
