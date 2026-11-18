using System.Globalization;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Connectors;
using Gatehouse.Application.Identity.IdentityProviders;
using Gatehouse.Domain.Connectors;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Domain.People;
using Gatehouse.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Tenancy.Onboarding;

/// <summary>
/// Onboarding status computed on read (<c>domain-model.md</c> §2.10), shared by the onboarding handlers and <c>/api/v1/me</c>;
/// skip and complete write <c>tenant_settings</c> keys <see cref="SkippedPrefix"/> and <see cref="CompletedKey"/>.
/// </summary>
public sealed class OnboardingStatusReadModel(
    ITenantDb db,
    ITenantContext tenant,
    IdentityProviderStore identityProviders,
    IConnectorRegistrar registrar,
    TimeProvider time)
{
    public const string CompletedKey = "onboarding.completed_at";
    public const string SkippedPrefix = "onboarding.skipped.";
    public static readonly IReadOnlyList<OnboardingStep> Skippable = [OnboardingStep.Connector, OnboardingStep.Staff];

    public async Task<OnboardingStatusDto> GetAsync(CancellationToken ct)
    {
        var settings = await db.TenantSettings.AsNoTracking()
            .Where(s => s.Key == CompletedKey || s.Key.StartsWith(SkippedPrefix))
            .ToListAsync(ct);
        var skipped = settings.Where(s => s.Key.StartsWith(SkippedPrefix, StringComparison.Ordinal))
            .Select(s => s.Key[SkippedPrefix.Length..]).ToHashSet(StringComparer.Ordinal);
        var completedAt = settings.FirstOrDefault(s => s.Key == CompletedKey) is { } c
            && DateTimeOffset.TryParse(c.Value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at)
                ? at
                : (DateTimeOffset?)null;

        var steps = new List<OnboardingStepDto>
        {
            await IdentityProviderAsync(ct),
            await SitesAsync(ct),
            await PeopleAsync(ct),
            await PacsAsync(ct),
            ApplySkip(await ConnectorAsync(ct), skipped),
            ApplySkip(await StaffAsync(ct), skipped),
        };
        return new OnboardingStatusDto(steps, CanComplete(steps), completedAt);
    }

    public async Task<bool> IsPendingAsync(CancellationToken ct) => !await db.TenantSettings.AnyAsync(s => s.Key == CompletedKey, ct);

    private static bool CanComplete(IEnumerable<OnboardingStepDto> steps) =>
        steps.All(s => s.State == OnboardingStepState.Complete || (s.Skippable && s.State == OnboardingStepState.Skipped));

    private static OnboardingStepDto ApplySkip(OnboardingStepDto step, HashSet<string> skipped) =>
        step.State != OnboardingStepState.Complete && skipped.Contains(step.Step.ToString())
            ? step with { State = OnboardingStepState.Skipped }
            : step;

    private static OnboardingStepDto Step(OnboardingStep step, OnboardingStepState state, string detail, string href) =>
        new(step, state, Skippable.Contains(step), detail, href);

    private async Task<OnboardingStepDto> IdentityProviderAsync(CancellationToken ct)
    {
        const string Href = "/admin/identity";
        const OnboardingStep Idp = OnboardingStep.IdentityProvider;
        var idp = await identityProviders.GetAsync(ct);
        return idp switch
        {
            null => Step(Idp, OnboardingStepState.NotStarted, "Connect your company's identity provider.", Href),
            { LastTestOk: true } => Step(Idp, OnboardingStepState.Complete, $"{idp.DisplayName} connected.", Href),
            _ => Step(Idp, OnboardingStepState.InProgress, "Saved, but the last connection test did not pass.", Href),
        };
    }

    private async Task<OnboardingStepDto> SitesAsync(CancellationToken ct)
    {
        const string Href = "/admin/sites";
        if (!await db.Sites.AnyAsync(ct))
        {
            return Step(OnboardingStep.Sites, OnboardingStepState.NotStarted, "Add your first site.", Href);
        }

        var complete = await db.Floors.AnyAsync(ct)
            && await (from d in db.Doors join a in db.Areas on d.AreaId equals a.Id select d.Id).AnyAsync(ct);
        return complete
            ? Step(OnboardingStep.Sites, OnboardingStepState.Complete, "Sites, floors, areas and doors are modelled.", Href)
            : Step(OnboardingStep.Sites, OnboardingStepState.InProgress, "Add a floor and at least one area with a door.", Href);
    }

    private async Task<OnboardingStepDto> PeopleAsync(CancellationToken ct)
    {
        const string Href = "/admin/people";
        var employees = await db.Persons.CountAsync(p => p.Type == PersonType.Employee && p.Status == PersonStatus.Active, ct);
        // Every area always keeps at least one owner (Area invariant), so the remaining rule is a second employee.
        return employees > 1
            ? Step(OnboardingStep.People, OnboardingStepState.Complete, $"{employees} employees.", Href)
            : Step(OnboardingStep.People, OnboardingStepState.NotStarted, "Add or import your employees.", Href);
    }

    private async Task<OnboardingStepDto> PacsAsync(CancellationToken ct)
    {
        const string Href = "/admin/pacs";
        var instances = await db.PacsInstances.AsNoTracking().ToListAsync(ct);
        if (instances.Count == 0)
        {
            return Step(OnboardingStep.Pacs, OnboardingStepState.NotStarted, "Register your access control system.", Href);
        }

        // Expected coverage is every access-controlled area of the tenant, not only areas on sites that already have a PACS (GH-ADMIN-02).
        var areas = await (from a in db.Areas
                           join f in db.Floors on a.FloorId equals f.Id
                           where a.AccessControlled
                           select new { a.Id, f.SiteId }).ToListAsync(ct);
        var mappings = await db.AccessLevelMappings.AsNoTracking().Select(m => new { m.AreaId, m.PacsInstanceId }).ToListAsync(ct);
        var uncoveredSites = areas.Where(a => !instances.Any(i => i.SiteIds.Contains(a.SiteId))).Select(a => a.SiteId).Distinct().Count();
        var unmapped = areas.Count(a => instances.Where(i => i.SiteIds.Contains(a.SiteId))
            .Any(i => !mappings.Any(m => m.AreaId == a.Id && m.PacsInstanceId == i.Id)));
        if (uncoveredSites > 0)
        {
            var detail = unmapped > 0 ? $" and {unmapped} area(s) are not mapped yet" : string.Empty;
            return Step(OnboardingStep.Pacs, OnboardingStepState.InProgress,
                $"{uncoveredSites} site(s) with access-controlled areas have no access control system{detail}.", Href);
        }

        return unmapped == 0
            ? Step(OnboardingStep.Pacs, OnboardingStepState.Complete, "Every access-controlled area is mapped to an access level.", Href)
            : Step(OnboardingStep.Pacs, OnboardingStepState.InProgress, $"{unmapped} area(s) are not mapped yet.", Href);
    }

    private async Task<OnboardingStepDto> ConnectorAsync(CancellationToken ct)
    {
        const string Href = "/admin/pacs";
        var connectorMode = await db.PacsInstances.AsNoTracking()
            .Where(p => p.FulfillmentMode == FulfillmentMode.Connector)
            .ToListAsync(ct);
        if (connectorMode.Count == 0)
        {
            return Step(OnboardingStep.Connector, OnboardingStepState.NotStarted, "Enroll a connector, or skip and fulfil manually.", Href);
        }

        var registrations = await registrar.ListAsync(tenant.TenantId, ct);
        var now = time.GetUtcNow();
        var ready = connectorMode.Count(i => i.IsOnline(now)
            && registrations.Any(r => r.PacsInstanceId == i.Id.Value && r.Status == ConnectorStatus.Active));
        return ready == connectorMode.Count
            ? Step(OnboardingStep.Connector, OnboardingStepState.Complete, "Every connector is enrolled and online.", Href)
            : Step(OnboardingStep.Connector, OnboardingStepState.InProgress,
                $"{connectorMode.Count - ready} connector(s) not enrolled or offline.", Href);
    }

    private async Task<OnboardingStepDto> StaffAsync(CancellationToken ct)
    {
        const string Href = "/admin/people";
        return await db.RoleAssignments.AnyAsync(r => r.Role == AssignedRole.SecurityStaff, ct)
            ? Step(OnboardingStep.Staff, OnboardingStepState.Complete, "Security staff assigned.", Href)
            : Step(OnboardingStep.Staff, OnboardingStepState.NotStarted, "Assign security staff, or skip.", Href);
    }
}
