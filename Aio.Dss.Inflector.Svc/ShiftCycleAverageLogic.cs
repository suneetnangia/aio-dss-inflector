using System.Text.Json;
using Aio.Dss.Inflector.Svc;

public class ShiftCycleAverageLogic : IInflectorActionLogic
{
    private readonly ILogger<ShiftCycleAverageLogic> _logger;

    // TODO - consider moving these keys to configurations passed in from the constructor
    private readonly string _dssKeyShiftsReference = "shiftsvalid";
    private readonly string _dssKeyLastTenShifts = "lastTenShifts";

    public ShiftCycleAverageLogic(ILogger<ShiftCycleAverageLogic> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<EgressHybridMessage> Execute(IngressHybridMessage message, IDataSource dataSource, IDataSink dataSink, CancellationToken cancellationToken)
    {
        try
        {
            List<CycleTime> lastTenShifts = new();
            ShiftReference? shiftReference = null;
            _logger.LogDebug("Received message: '{message}'", message.ActionRequestDataPayload.RootElement.ToString());

            if (message.ActionRequestDataPayload.RootElement.TryGetProperty("CycleTime", out JsonElement cycleTimeElement))
            {
                var cycleTime = JsonSerializer.Deserialize<CycleTime>(cycleTimeElement.GetRawText());
                if (cycleTime == null)
                {
                    throw new ArgumentNullException(nameof(cycleTime));
                }
                
                var dssLastTenShifts = await dataSource.ReadDataAsync(_dssKeyLastTenShifts, cancellationToken);
                if (dssLastTenShifts != null &&
                    dssLastTenShifts.RootElement.ValueKind == JsonValueKind.Array &&
                    dssLastTenShifts.RootElement.GetArrayLength() > 0)
                {
                    var dssLastTenShiftsString = dssLastTenShifts.RootElement.ToString();
                    lastTenShifts = !string.IsNullOrEmpty(dssLastTenShiftsString) ? JsonSerializer.Deserialize<List<CycleTime>>(dssLastTenShiftsString) ?? [] : [];
                }
                else
                {
                    _logger.LogDebug("No data found in DSS for key: '{key}'.", _dssKeyLastTenShifts);
                }

                if (lastTenShifts.Count >= 10)
                {
                    // Remove the oldest shift
                    lastTenShifts.RemoveAt(0);
                    _logger.LogDebug("Removed the oldest shift from the list.");
                }

                // calculate average of the 10 items (or less if just starting up)
                lastTenShifts.Add(cycleTime);
                var average = lastTenShifts.Average(x => x.Value);

                // Always add the latest shift and store into DSS
                // TODO - reflect on logic to ensure the same message is not added twice to the 10 items array - QoS1 allows duplicates
                await dataSink.PushDataAsync(_dssKeyLastTenShifts, JsonDocument.Parse(JsonSerializer.Serialize(lastTenShifts)), cancellationToken);

                // Enrich the message with shift information
                var referenceData = await dataSource.ReadDataAsync(_dssKeyShiftsReference, cancellationToken);
                if (referenceData != null && referenceData.RootElement.ValueKind == JsonValueKind.Array)
                {
                    var shiftData = JsonSerializer.Deserialize<List<ShiftReference>>(referenceData.RootElement.ToString());
                    if (shiftData == null)
                    {
                        _logger.LogWarning("No shift reference data found in DSS for key: '{key}'.", _dssKeyShiftsReference);
                        return null;
                    }
                    shiftReference = GetShiftFromTime(cycleTime.SourceTimestamp, shiftData);
                    if (shiftReference == null)
                    {
                        _logger.LogWarning("No shift data found for timestamp '{timestamp}'. Message can be discarded", cycleTime.SourceTimestamp);
                        return null;
                    }
                    _logger.LogDebug("Shift reference data found for timestamp '{timestamp}': '{shiftReference}'", cycleTime.SourceTimestamp, shiftReference);

                    // We are hardcoding the resulting message for now.
                    var responseActionPayload = new
                    {
                        specVersion = "1.0",
                        type = "StationAttribute.Value.Updated.v1",
                        source = "poc/localuns/microsoft/shiftCounter",
                        id = Guid.NewGuid(),
                        time = cycleTime.SourceTimestamp,
                        data = new
                        {
                            siteId = shiftReference.SiteId,
                            areaId = shiftReference.AreaId,
                            equipmentId = shiftReference.EquipmentId,
                            stationId = "302374d7-033d-45f9-990d-745680d96326",
                            stationAttributeId = "f263a248-cf54-4a9f-b54f-6101367c8775",
                            esmiGroupCode = 2,
                            esmiSubGroupCode = 3,
                            attributeName = "lr_ShiftCounter",
                            attributeValue = average,
                            attributeValueType = "double",
                            attributeTime = DateTime.UtcNow
                        }
                    };

                    return new EgressHybridMessage
                    {
                        ActionResponseDataPayload = JsonDocument.Parse(JsonSerializer.Serialize(responseActionPayload)),
                        CorrelationId = message.CorrelationId,
                        PassthroughPayload = message.PassthroughPayload
                    };

                }
                else
                {
                    _logger.LogWarning("No shift reference data found in DSS. Returning.");
                    return null;
                }
            }
            else
            {
                _logger.LogWarning("CycleTime property not found in ActionRequestDataPayload.");
                return null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message: '{message}'", message.ActionRequestDataPayload.RootElement.ToString());
            return null;
        }

    }

    private ShiftReference? GetShiftFromTime(DateTime sourceTimestamp, List<ShiftReference> shiftData)
    {
        TimeSpan time = sourceTimestamp.TimeOfDay;
        int timeInt = (int)time.TotalSeconds;

        var match = shiftData.FirstOrDefault(x => x.FromDayOfWeek == GetDayOfWeekInt(sourceTimestamp) && timeInt >= x.FromTimeSite.TotalSeconds && timeInt <= x.ToTimeSite.TotalSeconds);
        return match;
    }

    private int GetDayOfWeekInt(DateTime timestamp)
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

    public class CycleTime
    {
        public DateTime SourceTimestamp { get; set; }
        public int Value { get; set; }
    }
}