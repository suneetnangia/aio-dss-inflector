namespace Aio.Dss.Inflector.Svc.BusinessLogic.ShiftCounting;

public class ShiftCounter
{
    public Guid ShiftNumber { get; set; }

    public int DayOfWeek { get; set; }

    public TimeSpan StartTime { get; set; }

    public TimeSpan EndTime { get; set; }

    public int Value { get; set; }
}
