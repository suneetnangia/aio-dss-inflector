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

    public Worker(ILogger<Worker> logger, TelemetryReceiver<IngressHybridMessage> telemetryReceiver, Dictionary<string, IDataSource> dataSources, Dictionary<string, IDataSink> dataSinks)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        dataSinks = dataSinks ?? throw new ArgumentNullException(nameof(dataSinks));

        // Note: Do more checks for null here.
        _dssDataSource = dataSources["DssDataSource"];
        _dssDataSink = dataSinks["DssDataSink"];
        _mqttDataSink = dataSinks["MqttDataSink"];

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
        _logger.LogInformation("Received telemetry from {senderId}: {hybridMessage}", senderId, ingressHybridMessage);
        _ingressHybridMessages.Add(ingressHybridMessage);
        return Task.CompletedTask;
    }

    public async Task ProcessMessages(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var ingressHybridMessage = _ingressHybridMessages.Take(cancellationToken);
            EgressHybridMessage egressHybridMessage;

            _logger.LogInformation("'{action}' action received with data '{actiondatapayload}' for DSS.", ingressHybridMessage.Action, ingressHybridMessage.ActionRequestDataPayload);

            try
            {
                if (ingressHybridMessage.Action == InflectorAction.CycleTimeAverage)
                {
                    _logger.LogTrace("Processing Cycle Time Average...");
                    
                    // Add Logic for Average of last 10 cycle times, this is demo code. Replace with actual logic.
                    await _dssDataSink.PushDataAsync("123", ingressHybridMessage.ActionRequestDataPayload, cancellationToken);
                    var dssData = await _dssDataSource.ReadDataAsync("123", cancellationToken);

                    egressHybridMessage = new EgressHybridMessage
                    {
                        CorrelationId = ingressHybridMessage.CorrelationId,
                        ActionResponseDataPayload = dssData,
                        PassthroughPayload = ingressHybridMessage.PassthroughPayload
                    };

                    // Publish the data to the MQTT data sink for further processing outside of Inflector.                
                    await _mqttDataSink.PushDataAsync("aio-dss-inflector/data/cycletimeavg", JsonDocument.Parse(JsonSerializer.Serialize(egressHybridMessage)), cancellationToken);
                }
                else if (ingressHybridMessage.Action == InflectorAction.ShiftCounter)
                {
                    _logger.LogTrace("Processing Shift Counter...");

                    // Add Logic for Shift Counter
                    egressHybridMessage = new EgressHybridMessage
                    {
                        CorrelationId = ingressHybridMessage.CorrelationId,
                        ActionResponseDataPayload = ingressHybridMessage.ActionRequestDataPayload,
                        PassthroughPayload = ingressHybridMessage.PassthroughPayload
                    };

                    // Publish the data to the MQTT data sink for further processing outside of Inflector.                
                    await _mqttDataSink.PushDataAsync("aio-dss-inflector/data/shiftcounter", JsonDocument.Parse(JsonSerializer.Serialize(egressHybridMessage)), cancellationToken);
                }
                else
                {
                    _logger.LogWarning("Unknown action '{action}' received for DSS.", ingressHybridMessage.Action);

                    // Return the data to the MQTT data sink for further processing outside of Inflector.
                    await _mqttDataSink.PushDataAsync("aio-dss-inflector/data/egress", JsonDocument.Parse(JsonSerializer.Serialize(ingressHybridMessage)), cancellationToken);
                    continue;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing data for DSS.");
            }
        }
    }
}