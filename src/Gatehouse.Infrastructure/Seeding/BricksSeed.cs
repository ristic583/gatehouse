using Gatehouse.Domain.Fulfillment;
using Gatehouse.Domain.People;

namespace Gatehouse.Infrastructure.Seeding;

public static class BricksSeed
{
    public static SeedDefinition Definition { get; } = new(
        "Europe/Berlin",
        "bricks.test",
        [new("Berlin Office", "BER-OFF", [new(0, "Office floor")])],
        [
            new("BER-LOBBY", "Lobby", "BER-OFF", 0, "Low", 40, 40, 300, 560, "jan.koch"),
            new("BER-OFFICE", "Office", "BER-OFF", 0, "Low", 320, 40, 700, 560, "jan.koch"),
            new("BER-LAB", "Lab", "BER-OFF", 0, "Medium", 720, 40, 960, 280, "clara.fischer"),
            new("BER-SERVER", "Server Room", "BER-OFF", 0, "High", 720, 300, 960, 560, "clara.fischer"),
        ],
        [new("Berlin-Office", PacsVendor.CloudPacs, "BER-OFF")],
        new HashSet<string>(StringComparer.Ordinal),
        [
            new("jan.koch", "Jan", "Koch"),
            new("max.weber", "Max", "Weber", Manager: "jan.koch"),
            new("clara.fischer", "Clara", "Fischer", Manager: "jan.koch"),
            new("paul.wagner", "Paul", "Wagner", Manager: "jan.koch", Role: AssignedRole.SecurityStaff),
            new("mia.becker", "Mia", "Becker", Manager: "jan.koch", Role: AssignedRole.TenantAdmin),
            new("lena.schmidt", "Lena", "Schmidt", Manager: "max.weber"),
            new("tom.richter", "Tom", "Richter", PersonType.External, Sponsor: "max.weber"),
        ],
        new(
            ["Lukas", "Anna", "Felix", "Marie", "Jonas", "Sophie", "Paul", "Lea", "Leon", "Hannah"],
            ["Mueller", "Schmidt", "Schneider", "Fischer", "Meyer"],
            EvenManager: "max.weber",
            OddManager: "clara.fischer",
            ExternalSponsor: "max.weber",
            VisitorSponsor: "lena.schmidt",
            GuestSponsor: "mia.becker"),
        [
            new("lena.schmidt", "BER-LOBBY", SeedEntitlementKind.Confirmed, -30, 335),
            new("lena.schmidt", "BER-OFFICE", SeedEntitlementKind.Confirmed, -30, 335),
            new("lena.schmidt", "BER-LAB", SeedEntitlementKind.PendingAreaOwner, 1, 60),
        ],
        ThemeJson: """{"primary":"#7f1d1d","primaryForeground":"#ffffff","accent":"#"""
            + """fef2f2","accentForeground":"#7f1d1d","background":"#"""
            + """ffffff","foreground":"#1c1917","radiusPx":4,"terms":{"Area":"Zone"}}""");
}
