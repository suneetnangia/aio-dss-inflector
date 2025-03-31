namespace Aio.Dss.Inflector.Svc.BusinessLogic.Common;

public class ShiftReference
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    public Guid EquipmentId { get; set; }

    public Guid AreaId { get; set; }

    public Guid SiteId { get; set; }

    public int FromDayOfWeek { get; set; }

    public TimeSpan FromTimeSite { get; set; }

    public TimeSpan ToTimeSite { get; set; }
}
