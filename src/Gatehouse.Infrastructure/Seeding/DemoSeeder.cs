using Gatehouse.Application.Tenancy;
using Gatehouse.Domain.Access;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Credentials;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Domain.People;
using Gatehouse.Domain.Sites;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using FulfillmentEntity = Gatehouse.Domain.Fulfillment.Fulfillment;

namespace Gatehouse.Infrastructure.Seeding;

/// <summary>Deterministic demo data for the demo tenants (plan M2-08). Uses domain factories only.</summary>
public sealed class DemoSeeder(ITenantScopeFactory scopes, TimeProvider time, ILogger<DemoSeeder> logger)
{
    private const double PlanWidth = 1000;
    private const double PlanHeight = 600;

    private static readonly DateTimeOffset AuthoritativeStart = new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly (string Name, int Rank, ApproverChain Chain, int MaxDays)[] Levels =
    [
        ("Low", 1, ApproverChain.Manager, 365),
        ("Medium", 2, ApproverChain.AreaOwner, 365),
        ("High", 3, ApproverChain.Both, 182),
        ("Restricted", 4, ApproverChain.Both, 30),
    ];

    public static SeedDefinition? DefinitionFor(TenantId tenantId) => tenantId.Value switch
    {
        "nordlys" => NordlysSeed.Definition,
        "bricks" => BricksSeed.Definition,
        _ => null,
    };

    public async Task SeedAsync(TenantId tenantId, CancellationToken ct)
    {
        var tenant = tenantId.Value;

        if (DefinitionFor(tenantId) is not { } definition)
        {
            if (logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning("No demo seed exists for tenant {TenantId}", tenant);
            }

            return;
        }

        await using var scope = await scopes.CreateScopeAsync(tenantId, ct);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        await EnsureThemeAsync(db, definition, ct);
        await EnsureOutReadersAsync(db, ct);
        if (await db.Sites.AnyAsync(ct))
        {
            var rewritten = await RewriteCloudPacsRefsAsync(db, ct);
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation(
                    "Tenant {TenantId} already has sites, demo seed skipped ({Rewritten} CloudPACS refs updated)", tenant, rewritten);
            }

            return;
        }

        var seedTime = SeedTime();
        SeedStructure(db, definition, seedTime);
        await db.SaveChangesAsync(ct);
        await SeedEntitlementsAsync(db, definition, seedTime, ct);
        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("Seeded demo data for tenant {TenantId}", tenant);
        }
    }

    /// <summary>Adds an exit reader to every door that has none (M9 occupancy); also runs for databases seeded before M9.</summary>
    private static async Task EnsureOutReadersAsync(TenantDbContext db, CancellationToken ct)
    {
        var withOut = await db.Readers.Where(r => r.Direction == ReaderDirection.Out).Select(r => r.DoorId).ToListAsync(ct);
        var ins = await db.Readers.Where(r => r.Direction == ReaderDirection.In && !withOut.Contains(r.DoorId)).ToListAsync(ct);
        foreach (var reader in ins)
        {
            db.Readers.Add(
                Ok(
                    Reader.Create(
                        reader.DoorId, reader.Name + "-OUT", ReaderDirection.Out, new Point(reader.Position.X, reader.Position.Y + 24),
                        null)));
        }

        if (ins.Count > 0)
        {
            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>Adds the demo theme (M9) when the tenant has none; also runs for databases seeded before M9.</summary>
    private async Task EnsureThemeAsync(TenantDbContext db, SeedDefinition definition, CancellationToken ct)
    {
        if (definition.ThemeJson is null || await db.TenantSettings.AnyAsync(s => s.Key == "theme", ct))
        {
            return;
        }

        db.TenantSettings.Add(new Domain.Tenancy.TenantSetting("theme", definition.ThemeJson, SeedTime()));
        await db.SaveChangesAsync(ct);
    }

    public async Task ResetAccessDataAsync(TenantId tenantId, FulfillmentMode? modeOverride, CancellationToken ct)
    {
        var tenant = tenantId.Value;
        if (DefinitionFor(tenantId) is not { } definition)
        {
            if (logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning("No demo seed exists for tenant {TenantId}", tenant);
            }

            return;
        }

        await using var scope = await scopes.CreateScopeAsync(tenantId, ct);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        await db.StaffTasks.ExecuteDeleteAsync(ct);
        await db.ConnectorCommands.ExecuteDeleteAsync(ct);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM connector_partition_sequences", ct);
        await db.Fulfillments.ExecuteDeleteAsync(ct);
        await db.Entitlements.ExecuteDeleteAsync(ct);
        await db.OutboxMessages.ExecuteDeleteAsync(ct);
        foreach (var instance in await db.PacsInstances.ToListAsync(ct))
        {
            instance.SetFulfillmentMode(modeOverride ?? SeedModeFor(instance.Name));
        }

        await db.SaveChangesAsync(ct);
        await SeedEntitlementsAsync(db, definition, SeedTime(), ct);
        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("Reset demo access data for tenant {TenantId}", tenant);
        }
    }

    private static FulfillmentMode SeedModeFor(string pacsName) =>
        pacsName is "HQ-Copenhagen" or "Warehouse-Aarhus" ? FulfillmentMode.Connector : FulfillmentMode.Manual;

    /// <summary>Access level ref prefix per vendor (domain-model.md §2.5): LegacyPACS <c>AL-</c>, CloudPACS <c>AG-</c>.</summary>
    private static string AccessLevelRefFor(PacsVendor vendor, string areaCode) =>
        (vendor == PacsVendor.CloudPacs ? "AG-" : "AL-") + areaCode;

    /// <summary>Existing tenants seeded before M6: CloudPACS mappings still carry <c>AL-</c> refs. Idempotent.</summary>
    private static async Task<int> RewriteCloudPacsRefsAsync(TenantDbContext db, CancellationToken ct)
    {
        var cloudPacs = await db.PacsInstances.AsNoTracking()
            .Where(p => p.Vendor == PacsVendor.CloudPacs)
            .Select(p => p.Id)
            .ToListAsync(ct);
        return await db.AccessLevelMappings
            .Where(m => cloudPacs.Contains(m.PacsInstanceId) && m.VendorAccessLevelRef.StartsWith("AL-"))
#pragma warning disable CA1845 // expression tree translated to SQL; spans are not translatable
            .ExecuteUpdateAsync(u => u.SetProperty(m => m.VendorAccessLevelRef, m => "AG-" + m.VendorAccessLevelRef.Substring(3)), ct);
#pragma warning restore CA1845
    }

    private static T Ok<T>(Result<T> result) =>
        result.IsSuccess
            ? result.Value
            : throw new InvalidOperationException($"Demo seed is invalid: {result.Error.Code} {result.Error.Message}");

    private static void Ok(Result result)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"Demo seed is invalid: {result.Error.Code} {result.Error.Message}");
        }
    }

    private static void SeedStructure(TenantDbContext db, SeedDefinition d, DateTimeOffset seedTime)
    {
        var levels = Levels.ToDictionary(l => l.Name, l => Ok(SecurityLevel.Create(l.Name, l.Rank, l.Chain, l.MaxDays)));
        db.SecurityLevels.AddRange(levels.Values);

        var sites = new Dictionary<string, Site>(StringComparer.Ordinal);
        var floors = new Dictionary<(string, int), Floor>();
        foreach (var s in d.Sites)
        {
            var site = Ok(Site.Create(s.Name, s.Code, d.TimeZoneId));
            sites[s.Code] = site;
            foreach (var f in s.Floors)
            {
                floors[(s.Code, f.Level)] = Ok(Floor.Create(site.Id, f.Name, f.Level, (int)PlanWidth, (int)PlanHeight));
            }
        }

        db.Sites.AddRange(sites.Values);
        db.Floors.AddRange(floors.Values);

        var people = SeedPeople(d, seedTime);
        db.Persons.AddRange(people.Values);
        var tenantDigit = ReferenceEquals(d, NordlysSeed.Definition) ? 1 : 2;
        var index = 0;
        foreach (var person in people.Values)
        {
            index++;
            db.Cards.Add(Ok(Card.CreateActive(person.Id, $"1000{tenantDigit}{index:00000}", seedTime)));
        }

        foreach (var named in d.NamedPeople.Where(p => p.Role is not null))
        {
            db.RoleAssignments.Add(Ok(RoleAssignment.Create(people[named.Username].Id, named.Role!.Value, null)));
        }

        var pacs = d.PacsInstances.ToDictionary(
            p => p.SiteCode,
            p =>
            {
                var instance = Ok(PacsInstance.Create(p.Name, p.Vendor, [sites[p.SiteCode].Id]));
                instance.SetFulfillmentMode(SeedModeFor(p.Name));
                return instance;
            });
        db.PacsInstances.AddRange(pacs.Values);

        foreach (var a in d.Areas)
        {
            var polygon = Ok(Polygon.Create([new(a.X1, a.Y1), new(a.X2, a.Y1), new(a.X2, a.Y2), new(a.X1, a.Y2)], PlanWidth, PlanHeight));
            var owner = people[a.Owner];
            var area = Ok(
                Area.Create(floors[(a.SiteCode, a.Level)].Id, a.Name, a.Code, polygon, levels[a.SecurityLevel].Id, owner.Id, owner.Type));
            db.Areas.Add(area);

            var doorPosition = new Point((a.X1 + a.X2) / 2, a.Y2);
            var door = Ok(Door.Create(area.Id, a.Name + " door", doorPosition, "D-" + a.Code));
            db.Doors.Add(door);
            db.Readers.Add(
                Ok(Reader.Create(door.Id, "R-" + a.Code, ReaderDirection.In, new Point(doorPosition.X, doorPosition.Y - 12), null)));
            db.Readers.Add(
                Ok(
                    Reader.Create(
                        door.Id, "R-" + a.Code + "-OUT", ReaderDirection.Out, new Point(doorPosition.X, doorPosition.Y + 12), null)));

            if (!d.UnmappedAreaCodes.Contains(a.Code))
            {
                db.AccessLevelMappings.Add(
                    Ok(
                        AccessLevelMapping.Create(
                            area.Id, pacs[a.SiteCode].Id, AccessLevelRefFor(pacs[a.SiteCode].Vendor, a.Code), a.Name)));
            }
        }
    }

    private static Dictionary<string, Person> SeedPeople(SeedDefinition d, DateTimeOffset seedTime)
    {
        var people = new Dictionary<string, Person>(StringComparer.Ordinal);
        var open = Ok(Eligibility.Create(AuthoritativeStart, null, d.TimeZoneId));

        // Named people are ordered so managers and sponsors come first.
        foreach (var p in d.NamedPeople)
        {
            var local = p.Type != PersonType.Employee;
            var eligibility = local ? Ok(Eligibility.Create(seedTime.AddDays(-10), seedTime.AddDays(180), d.TimeZoneId)) : open;
            people[p.Username] = Ok(Person.Create(
                local ? PersonSource.Local : PersonSource.Authoritative,
                p.Type,
                p.GivenName,
                p.FamilyName,
                $"{p.Username}@{d.EmailDomain}",
                eligibility,
                p.Manager is null ? null : people[p.Manager].Id,
                p.Sponsor is null ? null : people[p.Sponsor],
                seedTime));
        }

        var g = d.Generated;
        for (var i = 1; i <= 53; i++)
        {
            var given = g.GivenNames[(i - 1) % 10];
            var family = g.FamilyNames[((i - 1) / 10) % 5];
            var email = $"person-{i:00}@{d.EmailDomain}";
            var (type, sponsor, start, end) = i switch
            {
                <= 40 => (PersonType.Employee, (string?)null, AuthoritativeStart, (DateTimeOffset?)null),
                <= 44 => (PersonType.External, g.ExternalSponsor, seedTime.AddDays(-10), seedTime.AddDays(180)),
                <= 52 => (PersonType.Visitor, g.VisitorSponsor, seedTime, seedTime.AddDays(7)),
                _ => (PersonType.Guest, g.GuestSponsor, seedTime, seedTime.AddDays(1)),
            };
            var manager = type == PersonType.Employee ? people[i % 2 == 0 ? g.EvenManager : g.OddManager].Id : (PersonId?)null;
            people[$"person-{i:00}"] = Ok(Person.Create(
                type == PersonType.Employee ? PersonSource.Authoritative : PersonSource.Local,
                type,
                given,
                family,
                email,
                Ok(Eligibility.Create(start, end, d.TimeZoneId)),
                manager,
                sponsor is null ? null : people[sponsor],
                seedTime));
        }

        return people;
    }

    private static async Task SeedEntitlementsAsync(TenantDbContext db, SeedDefinition d, DateTimeOffset seedTime, CancellationToken ct)
    {
        var emails = d.Entitlements.Select(e => $"{e.Subject}@{d.EmailDomain}").ToHashSet(StringComparer.Ordinal);
        var subjects = await db.Persons.Where(p => emails.Contains(p.Email)).ToDictionaryAsync(p => p.Email, ct);
        var areaCodes = d.Entitlements.Select(e => e.AreaCode).ToHashSet(StringComparer.Ordinal);
        var areas = await db.Areas.Where(a => areaCodes.Contains(a.Code)).ToDictionaryAsync(a => a.Code, ct);
        var levels = await db.SecurityLevels.ToDictionaryAsync(l => l.Id, ct);
        var areaIds = areas.Values.Select(a => a.Id).ToList();
        var mappings = await db.AccessLevelMappings.Where(m => areaIds.Contains(m.AreaId)).ToListAsync(ct);
        var staffId = await db.RoleAssignments
            .Where(r => r.Role == AssignedRole.SecurityStaff)
            .Select(r => r.PersonId)
            .FirstAsync(ct);

        foreach (var e in d.Entitlements)
        {
            var subject = subjects[$"{e.Subject}@{d.EmailDomain}"];
            var area = areas[e.AreaCode];
            var kinds = ApprovalRouter.BuildSteps(levels[area.SecurityLevelId].ApproverChain);
            var plan = kinds
                .Select(k => new StepPlan(k, k == ApproverKind.Manager ? subject.ManagerId ?? subject.SponsorId : null))
                .ToList();
            var entitlement = Entitlement.Create(
                subject.Id,
                area.Id,
                subject.Id,
                seedTime.AddDays(e.StartDays),
                seedTime.AddDays(e.EndDays),
                seedTime.AddDays(e.EndDays),
                "Demo seed: access needed for daily work",
                plan,
                seedTime);
            db.Entitlements.Add(entitlement);

            var stepsToApprove = e.Kind switch
            {
                SeedEntitlementKind.Confirmed => plan.Count,
                SeedEntitlementKind.PendingManager => 0,
                SeedEntitlementKind.PendingAreaOwner => kinds.ToList().IndexOf(ApproverKind.AreaOwner),
                _ => 0,
            };

            for (var i = 0; i < stepsToApprove; i++)
            {
                var step = entitlement.Steps[i];
                var approver = step.ApproverKind == ApproverKind.Manager
                    ? step.AssignedApproverId!.Value
                    : area.Owners.First(o => o.PersonId != subject.Id).PersonId;
                Ok(entitlement.Approve(step.Id, approver, null, seedTime));
            }

            if (e.Kind == SeedEntitlementKind.Confirmed)
            {
                foreach (var mapping in mappings.Where(m => m.AreaId == area.Id))
                {
                    var fulfillment = FulfillmentEntity.Create(
                        entitlement.Id, mapping.PacsInstanceId, FulfillmentKind.Grant, CommandPriority.Normal, seedTime);
                    Ok(fulfillment.MarkPending(null, seedTime));
                    Ok(fulfillment.ConfirmByStaff(staffId, seedTime));
                    db.Fulfillments.Add(fulfillment);
                }
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private DateTimeOffset SeedTime()
    {
        var now = time.GetUtcNow();
        return new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, TimeSpan.Zero);
    }
}
