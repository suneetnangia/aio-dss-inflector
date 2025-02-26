using System.Text.Json;
using Aio.Dss.Inflector.Svc;

public class TotalCounterLogic : BaseLogic, IInflectorActionLogic
{
    private readonly ILogger<TotalCounterLogic> _logger;
    private readonly string _dssKeyShiftsReference = "shifts";
    private readonly string _dssKeyLkvShiftCounter = "lkvShiftCounter";
    private readonly string _dssKeyPreviousShiftCounter = "previousShiftCounter";

    public TotalCounterLogic(ILogger<TotalCounterLogic> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<EgressHybridMessage> Execute(IngressHybridMessage message, IDataSource dataSource, IDataSink dataSink, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogTrace("Received message: '{message}'", message.ActionRequestDataPayload.RootElement.ToString());

            if (message.ActionRequestDataPayload.RootElement.TryGetProperty("TotalCounter", out JsonElement totalCounterElement))
            {
                var totalCounter = JsonSerializer.Deserialize<TotalCounter>(totalCounterElement.GetRawText());
                if (totalCounter == null)
                {
                    throw new ArgumentNullException(nameof(totalCounter));
                }

                var referenceData = await dataSource.ReadDataAsync(_dssKeyShiftsReference, cancellationToken);
                _logger.LogTrace("Reference data found in DSS for key: '{key}': '{root}'", _dssKeyShiftsReference, referenceData.RootElement.ToString());
                if (referenceData != null && referenceData.RootElement.ValueKind == JsonValueKind.Array)
                {
                    var shiftData = JsonSerializer.Deserialize<List<ShiftReference>>(referenceData.RootElement.ToString());

                    if (shiftData == null || !shiftData.Any())
                    {
                        _logger.LogWarning("No shift reference data found in DSS for key: '{reference}'.", _dssKeyShiftsReference);
                        throw new InvalidOperationException(string.Format("No shift reference data found in DSS for key: '{0}'.", _dssKeyShiftsReference));
                    }

                    var shiftReference = GetShiftFromTime(totalCounter.SourceTimestamp, shiftData);
                    if (shiftReference == null)
                    {
                        _logger.LogWarning("No shift data found for timestamp '{totalCounter.SourceTimestamp}'. Message can be discarded", totalCounter.SourceTimestamp);
                        throw new InvalidOperationException(string.Format("No shift data found for timestamp '{timestamp}'.", totalCounter.SourceTimestamp));
                    }

                    _logger.LogTrace("Shift reference data found for timestamp '{timestamp}': '{reference}'", totalCounter.SourceTimestamp, shiftReference);

                    var currentShiftCounter = new ShiftCounter
                    {
                        ShiftNumber = shiftReference.Id,
                        DayOfWeek = shiftReference.FromDayOfWeek,
                        StartTime = shiftReference.FromTimeSite,
                        EndTime = shiftReference.ToTimeSite,
                        Value = totalCounter.Value
                    };

                    var lkvShiftCounter = await dataSource.ReadDataAsync(_dssKeyLkvShiftCounter, cancellationToken);
                    int lastLoggedPreviousShiftTotalCounterValue = 0;

                    if (lkvShiftCounter == null)
                    {
                        // First shift, set the previous to current as we are just starting off
                        await dataSink.PushDataAsync(_dssKeyPreviousShiftCounter, JsonDocument.Parse(JsonSerializer.Serialize(currentShiftCounter)), cancellationToken);
                    }
                    else
                    {
                        var lkvShiftCounterData = JsonSerializer.Deserialize<ShiftCounter>(lkvShiftCounter.RootElement.ToString());

                        if (lkvShiftCounterData != null && lkvShiftCounterData.ShiftNumber == currentShiftCounter.ShiftNumber)
                        {
                            // Same shift
                            var previousShiftCounter = await dataSource.ReadDataAsync(_dssKeyPreviousShiftCounter, cancellationToken);
                            if (previousShiftCounter != null)
                            {
                                var previousShiftCounterData = JsonSerializer.Deserialize<ShiftCounter>(previousShiftCounter.RootElement.ToString());
                                if (previousShiftCounterData != null)
                                {
                                    lastLoggedPreviousShiftTotalCounterValue = previousShiftCounterData.Value;
                                }
                            }
                        }
                        else
                        {
                            // We are starting next shift, get the LKV and store to previous shift value
                            if (lkvShiftCounterData != null)
                            {
                                lastLoggedPreviousShiftTotalCounterValue = lkvShiftCounterData.Value;
                            }
                            await dataSink.PushDataAsync(_dssKeyPreviousShiftCounter, JsonDocument.Parse(JsonSerializer.Serialize(lkvShiftCounterData)), cancellationToken);
                        }
                    }

                    // Always update the LKV
                    await dataSink.PushDataAsync(_dssKeyLkvShiftCounter, JsonDocument.Parse(JsonSerializer.Serialize(currentShiftCounter)), cancellationToken);

                    var shiftCounterValue = totalCounter.Value - lastLoggedPreviousShiftTotalCounterValue;

                    var responseActionPayload = new
                    {
                        action = "result",
                        payload = new
                        {
                            specVersion = "1.0",
                            type = "StationAttribute.Value.Updated.v1",
                            source = "poc/localuns/microsoft/shiftCounter",
                            id = Guid.NewGuid(),
                            time = totalCounter.SourceTimestamp,
                            data = new
                            {
                                siteId = shiftReference.SiteId,
                                areaId = shiftReference.AreaId,
                                equipmentId = shiftReference.EquipmentId,
                                stationId = "302374d7-033d-45f9-990d-745680d96326",
                                stationAttributeId = "c27063cf-f24f-425b-af0c-b8298bd6fd67",
                                esmiGroupCode = 2,
                                esmiSubGroupCode = 3,
                                attributeName = "lr_ShiftCounter",
                                attributeValue = shiftCounterValue,
                                attributeValueType = "double",
                                attributeTime = DateTime.UtcNow
                            }
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
                    throw new InvalidOperationException("No shift reference data found in DSS.");
                }
            }
            else
            {
                _logger.LogWarning("TotalCounter property not found in ActionRequestDataPayload.");
                throw new InvalidOperationException("TotalCounter property not found in ActionRequestDataPayload.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message: '{0}'", message.ActionRequestDataPayload.RootElement.ToString());
            throw;
        }
    }

    public class TotalCounter
    {
        public DateTime SourceTimestamp { get; set; }
        public int Value { get; set; }
    }

    public class ShiftCounter
    {
        public Guid ShiftNumber { get; set; }
        public int DayOfWeek { get; set; }
        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }
        public int Value { get; set; }
    }
}