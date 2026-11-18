using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.LiveBuilding.Dtos;
using Gatehouse.Domain.LiveBuilding;
using Gatehouse.Domain.Sites;

namespace Gatehouse.Application.LiveBuilding.ListAlarms;

/// <summary>Alarm rows with door, area, site and acknowledger names (shared by list and acknowledge); flat member-init so EF can
/// filter/sort.</summary>
internal static class AlarmProjection
{
    public static IQueryable<AlarmRow> Rows(ITenantDb db, IQueryable<Alarm> alarms) =>
        from a in alarms
        join d in db.Doors on a.DoorId equals d.Id
        join ar in db.Areas on d.AreaId equals ar.Id
        join f in db.Floors on ar.FloorId equals f.Id
        select new AlarmRow
        {
            AlarmId = a.Id,
            DoorId = d.Id,
            DoorName = d.Name,
            AreaId = ar.Id,
            AreaName = ar.Name,
            SiteId = f.SiteId,
            Kind = a.Kind,
            RaisedAt = a.RaisedAt,
            DoorEventId = a.DoorEventId,
            AcknowledgedAt = a.AcknowledgedAt,
            AcknowledgedByName = db.Persons.Where(p => p.Id == a.AcknowledgedById)
                .Select(p => p.GivenName + " " + p.FamilyName)
                .FirstOrDefault(),
            Comment = a.Comment,
        };

    public static AlarmDto ToDto(this AlarmRow r) => new(
        r.AlarmId.Value, r.DoorId.Value, r.DoorName, r.AreaId.Value, r.AreaName, r.SiteId.Value, r.Kind, r.RaisedAt, r.DoorEventId,
        r.AcknowledgedAt, r.AcknowledgedByName, r.Comment);
}
