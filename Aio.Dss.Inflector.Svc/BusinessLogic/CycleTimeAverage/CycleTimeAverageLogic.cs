namespace Aio.Dss.Inflector.Svc.BusinessLogic.CycleTimeAverage;

using System.Text.Json;
using Aio.Dss.Inflector.Svc;
using Aio.Dss.Inflector.Svc.BusinessLogic.Common;

public class CycleTimeAverageLogic : Logic, IInflectorActionLogic
{
    private readonly ILogger<CycleTimeAverageLogic> _logger;
    private readonly string _dssKeyShiftsReference;
    private readonly string _dssKeyLastTenShifts;

    public CycleTimeAverageLogic(ILogger<CycleTimeAverageLogic> logger, string dssKeyShiftsReference = "shifts", string dssKeyLastTenShifts = "lastTenShifts")
    {
        _dssKeyShiftsReference = dssKeyShiftsReference;
        _dssKeyLastTenShifts = dssKeyLastTenShifts;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<EgressHybridMessage> Execute(IngressHybridMessage message, IDataSource dataSource, IDataSink dataSink, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(dataSink);
        ArgumentNullException.ThrowIfNull(cancellationToken);

        try
        {
            List<CycleTime> lastTenShifts = new();
            ShiftReference? shiftReference = null;

            _logger.LogDebug("Received message: '{payload}'", message.ActionRequestDataPayload.RootElement.ToString());

            if (message.ActionRequestDataPayload.RootElement.TryGetProperty("CycleTime", out JsonElement cycleTimeElement))
            {
                var cycleTime = JsonSerializer.Deserialize<CycleTime>(cycleTimeElement.GetRawText());
                if (cycleTime == null)
                {
                    throw new ApplicationException("CycleTime data is null.");
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
                    // This is the first time we are running, so expected behavior
                    _logger.LogDebug("No data found in DSS for key: '{key}'.", _dssKeyLastTenShifts);
                }

                if (lastTenShifts.Count >= 10)
                {
                    // Remove the oldest shift
                    lastTenShifts.RemoveAt(0);
                    _logger.LogDebug("Removed the oldest shift from the list.");
                }

                // Note - reflect on logic to ensure the same message is not added twice to the 10 items array - QoS1 allows duplicates
                lastTenShifts.Add(cycleTime);

                // calculate average of the 10 items (or less if just starting up)
                var average = lastTenShifts.Average(x => x.Value);

                await dataSink.PushDataAsync(_dssKeyLastTenShifts, JsonDocument.Parse(JsonSerializer.Serialize(lastTenShifts)), cancellationToken);

                var referenceData = await dataSource.ReadDataAsync(_dssKeyShiftsReference, cancellationToken);

                _logger.LogDebug("Reference data found in DSS for key: '{reference}': '{root}'", _dssKeyShiftsReference, referenceData.RootElement.ToString());
                if (referenceData != null && referenceData.RootElement.ValueKind == JsonValueKind.Array)
                {
                    var shiftData = JsonSerializer.Deserialize<List<ShiftReference>>(referenceData.RootElement.ToString());

                    if (shiftData != null)
                    {
                        shiftReference = GetShiftFromTime(cycleTime.SourceTimestamp, shiftData);
                        if (shiftReference == null)
                        {
                            throw new InvalidOperationException($"No shift data found for timestamp 'cycleTime.SourceTimestamp'.");
                        }

                        _logger.LogDebug("Shift reference data found for timestamp '{0}': '{1}'", cycleTime.SourceTimestamp, shiftReference);

                        // We are hardcoding the resulting message for now...
                        var responseActionPayload = new
                        {
                            action = "result",
                            payload = new
                            {
                                specVersion = "1.0",
                                id = Guid.NewGuid(),
                                time = cycleTime.SourceTimestamp,
                                data = new
                                {
                                    siteId = shiftReference.SiteId,
                                    areaId = shiftReference.AreaId,
                                    equipmentId = shiftReference.EquipmentId,
                                    attributeName = "AvgCycleTime",
                                    attributeValue = average,
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
                        throw new InvalidOperationException("No shift reference data found in DSS.");
                    }
                }
                else
                {
                    throw new InvalidOperationException("No shift reference data found in DSS.");
                }
            }
            else
            {
                throw new InvalidOperationException("CycleTime property not found in ActionRequestDataPayload.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message: '{message.ActionRequestDataPayload}'", message.ActionRequestDataPayload.RootElement.ToString());
            throw;
        }
    }

    public class CycleTime
    {
        public DateTime SourceTimestamp { get; set; }

        public int Value { get; set; }
    }
}
