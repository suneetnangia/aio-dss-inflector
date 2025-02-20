using System.Text.Json;

public abstract class BaseLogic
{
    protected ShiftReference? GetShiftFromTime(DateTime sourceTimestamp, List<ShiftReference> shiftData)
    {
        TimeSpan time = sourceTimestamp.TimeOfDay;
        int timeInt = (int)time.TotalSeconds;

        var match = shiftData.FirstOrDefault(x => x.FromDayOfWeek == GetDayOfWeekInt(sourceTimestamp) && timeInt >= x.FromTimeSite.TotalSeconds && timeInt <= x.ToTimeSite.TotalSeconds);
        return match;
    }

    protected int GetDayOfWeekInt(DateTime timestamp)
    {
        return timestamp.DayOfWeek switch
        {
            DayOfWeek.Monday => 1,
            DayOfWeek.Tuesday => 2,
            DayOfWeek.Wednesday => 3,
            DayOfWeek.Thursday => 4,
            DayOfWeek.Friday => 5,
            _ => 0 // Return 0 for Saturday and Sunday
        };
    }

    protected List<ShiftReference> ParseJsonLines(string jsonLines)
    {
        var shiftData = new List<ShiftReference>();
        using (var reader = new StringReader(jsonLines))
        {
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                var shiftReference = JsonSerializer.Deserialize<ShiftReference>(line);
                if (shiftReference != null)
                {
                    shiftData.Add(shiftReference);
                }
            }
        }
        return shiftData;
    }
}
