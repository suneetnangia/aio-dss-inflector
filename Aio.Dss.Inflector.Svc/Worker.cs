namespace Aio.Dss.Inflector.Svc;

using System.Collections.Concurrent;
using System.Text.Json;
using Azure.Iot.Operations.Protocol.Telemetry;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

public sealed class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    TelemetryReceiver<IngressHybridMessage> _telemetryReceiver;
    private readonly IDataSource _dssDataSource;
    private readonly IDataSink _dssDataSink;
    private readonly IDataSink _mqttDataSink;
    private readonly BlockingCollection<IngressHybridMessage> _ingressHybridMessages;
    private readonly IInflectorActionLogic _logicCycleTimeAverage;
    private readonly IInflectorActionLogic _logicShiftCounter;

    public Worker(ILogger<Worker> logger,
        TelemetryReceiver<IngressHybridMessage> telemetryReceiver,
        Dictionary<string, IDataSource> dataSources,
        Dictionary<string, IDataSink> dataSinks,
        Dictionary<string, IInflectorActionLogic> inflectorActionLogics)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        dataSinks = dataSinks ?? throw new ArgumentNullException(nameof(dataSinks));
        inflectorActionLogics = inflectorActionLogics ?? throw new ArgumentNullException(nameof(inflectorActionLogics));

        // Note: Do more checks for null here.
        _dssDataSource = dataSources["DssDataSource"];
        _dssDataSink = dataSinks["DssDataSink"];
        _mqttDataSink = dataSinks["MqttDataSink"];
        _logicCycleTimeAverage = inflectorActionLogics["CycleTimeAverage"];
        _logicShiftCounter = inflectorActionLogics["ShiftCounter"];

        _telemetryReceiver = telemetryReceiver ?? throw new ArgumentNullException(nameof(telemetryReceiver));
        _ingressHybridMessages = new BlockingCollection<IngressHybridMessage>();
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Initiating the inflector: {time}", DateTimeOffset.Now);

        // Start the message receiver
        _telemetryReceiver.OnTelemetryReceived = ReceiveIngressHybridMessage;
        await _telemetryReceiver.StartAsync(cancellationToken);

        // Enter main loop to process the sensor data
        await ProcessMessages(cancellationToken);
    }

    public Task ReceiveIngressHybridMessage(string senderId, IngressHybridMessage ingressHybridMessage, IncomingTelemetryMetadata metadata)
    {
        _logger.LogTrace("Received telemetry from {senderId}: {hybridMessage}", senderId, ingressHybridMessage);
        _ingressHybridMessages.Add(ingressHybridMessage);
        return Task.CompletedTask;
    }

    public async Task ProcessMessages(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var ingressHybridMessage = _ingressHybridMessages.Take(cancellationToken);
            EgressHybridMessage egressHybridMessage;

            _logger.LogTrace("'{action}' action received with data '{actiondatapayload}' for DSS.", ingressHybridMessage.Action, ingressHybridMessage.ActionRequestDataPayload.RootElement.ToString());
            
            try
            {
                switch (ingressHybridMessage.Action)
                {
                    case InflectorAction.CycleTimeAverage:
                        egressHybridMessage = await _logicCycleTimeAverage.Execute(ingressHybridMessage, _dssDataSource, _dssDataSink, cancellationToken);
                        break;
                    case InflectorAction.ShiftCounter:
                        egressHybridMessage = await _logicShiftCounter.Execute(ingressHybridMessage, _dssDataSource, _dssDataSink, cancellationToken);
                        break;
                    default:
                         _logger.LogWarning("Unknown action '{action}' received for DSS.", ingressHybridMessage.Action);
                        // Return the data to the MQTT data sink for further processing outside of Inflector.
                        await _mqttDataSink.PushDataAsync("aio-dss-inflector/data/egress", JsonDocument.Parse(JsonSerializer.Serialize(ingressHybridMessage)), cancellationToken);
                        continue;
                }

                // Publish the data to the MQTT data sink for further processing outside of Inflector.
                _logger.LogTrace($"Publishing {ingressHybridMessage.Action} to MQTT data sink topic...");
                await _mqttDataSink.PushDataAsync("aio-dss-inflector/data/egress", JsonDocument.Parse(JsonSerializer.Serialize(egressHybridMessage)), cancellationToken);
            }
            catch (Exception ex)
            {
                // TODO evaluate also returning the ingress message in case of failure
                _logger.LogError(ex, "Error processing data for DSS.");
            }
        }
    }
}