namespace Aio.Dss.Inflector.Svc;

using System.Collections.Concurrent;
using System.Text.Json;
using Azure.Iot.Operations.Protocol.Telemetry;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

public sealed class Worker : BackgroundService
{
    private const int MESSAGE_BUFFER_SIZE = 1000;
    private readonly ILogger<Worker> _logger;
    private readonly TelemetryReceiver<IngressHybridMessage> _telemetryReceiver;
    private readonly IDataSource _dssDataSource;
    private readonly IDataSink _dssDataSink;
    private readonly IDataSink _mqttDataSink;
    private readonly BlockingCollection<IngressHybridMessage> _ingressHybridMessages;
    private readonly Dictionary<InflectorAction, IInflectorActionLogic> _inflectorActionLogicItems;
    private readonly string _mqttDataSinkTopic;

    public Worker(ILogger<Worker> logger,
        TelemetryReceiver<IngressHybridMessage> telemetryReceiver,
        [FromKeyedServices(Constants.DSS_DATA_SOURCE_KEY)] IDataSource dssDataSource,
        [FromKeyedServices(Constants.MQTT_DATA_SINK_KEY)] IDataSink mqttDataSink,
        [FromKeyedServices(Constants.DSS_DATA_SINK_KEY)] IDataSink dssDataSink,
        [FromKeyedServices(Constants.MQTT_EGRESS_TOPIC)] string mqttDataSinkTopic,
        Dictionary<InflectorAction, IInflectorActionLogic> inflectorActionLogics)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _telemetryReceiver = telemetryReceiver ?? throw new ArgumentNullException(nameof(telemetryReceiver));
        _dssDataSource = dssDataSource ?? throw new ArgumentNullException(nameof(dssDataSource));
        _mqttDataSink = mqttDataSink ?? throw new ArgumentNullException(nameof(mqttDataSink));
        _dssDataSink = dssDataSink ?? throw new ArgumentNullException(nameof(dssDataSink));
        _mqttDataSinkTopic = mqttDataSinkTopic ?? throw new ArgumentNullException(nameof(mqttDataSinkTopic));
        _inflectorActionLogicItems = inflectorActionLogics ?? throw new ArgumentNullException(nameof(inflectorActionLogics));

        // Add capacity limit to prevent unbounded growth of buffered messages
        _ingressHybridMessages = new BlockingCollection<IngressHybridMessage>(boundedCapacity: MESSAGE_BUFFER_SIZE);
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Initiating the inflector: {time}", DateTimeOffset.UtcNow);

        try
        {
            // Start the message receiver
            _telemetryReceiver.OnTelemetryReceived = ReceiveIngressHybridMessage;
            await _telemetryReceiver.StartAsync(cancellationToken);

            // Enter main loop to process the messages
            await ProcessMessages(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown, no action needed
            _logger.LogInformation("Worker service shutting down");
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Fatal error in worker service");
            throw;
        }
    }

    public Task ReceiveIngressHybridMessage(string senderId, IngressHybridMessage ingressHybridMessage, IncomingTelemetryMetadata metadata)
    {
        _logger.LogTrace("Received telemetry from {senderId}: {hybridMessage}", senderId, ingressHybridMessage);
        _ingressHybridMessages.Add(ingressHybridMessage);

        // Note: message is acknowledged by default once it's added to the collection.
        // If there's a fatal crash beyond this point, other than retry logic, message will not be lost.        
        return Task.CompletedTask;
    }

    public async Task ProcessMessages(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var ingressHybridMessage = _ingressHybridMessages.Take(cancellationToken);
            EgressHybridMessage egressHybridMessage;

            _logger.LogTrace("'{action}' action received with data '{actiondatapayload}' for DSS.",
                ingressHybridMessage.Action,
                ingressHybridMessage.ActionRequestDataPayload.RootElement.ToString());

            try
            {
                if (!_inflectorActionLogicItems.TryGetValue(ingressHybridMessage.Action, out var actionLogic))
                {
                    _logger.LogWarning("Unknown action '{action}' received for DSS, returning the message received.", ingressHybridMessage.Action);

                    // Return the data to the MQTT data sink for further processing outside of DSS Inflector.
                    await _mqttDataSink.PushDataAsync(_mqttDataSinkTopic,
                        JsonDocument.Parse(JsonSerializer.Serialize(ingressHybridMessage)),
                        cancellationToken);
                    continue;
                }

                egressHybridMessage = await actionLogic.Execute(ingressHybridMessage, _dssDataSource, _dssDataSink, cancellationToken);

                // Publish the data to the MQTT data sink for further processing outside of Inflector.
                _logger.LogTrace($"Publishing {ingressHybridMessage.Action} to MQTT data sink topic...");
                await _mqttDataSink.PushDataAsync(_mqttDataSinkTopic,
                    JsonDocument.Parse(JsonSerializer.Serialize(egressHybridMessage)),
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing action '{action}' for DSS.", ingressHybridMessage.Action);

                try
                {
                    // Return error message to MQTT topic
                    var errorMessage = new EgressHybridMessage
                    {
                        CorrelationId = ingressHybridMessage.CorrelationId,
                        ActionResponseDataPayload = JsonDocument.Parse(JsonSerializer.Serialize(new
                        {
                            Error = ex.Message
                        })),
                        PassthroughPayload = JsonDocument.Parse(JsonSerializer.Serialize(ingressHybridMessage))
                    };

                    await _mqttDataSink.PushDataAsync(
                        $"{_mqttDataSinkTopic}",
                        JsonDocument.Parse(JsonSerializer.Serialize(errorMessage)),
                        cancellationToken);
                }
                catch (Exception innerEx)
                {
                    _logger.LogError(innerEx, "Failed to publish error message to MQTT");
                }
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping worker service...");

        // Stop the telemetry receiver
        if (_telemetryReceiver != null)
        {
            await _telemetryReceiver.StopAsync(cancellationToken);
        }

        await base.StopAsync(cancellationToken);
    }

    public override void Dispose()
    {
        _ingressHybridMessages?.Dispose();
        base.Dispose();
    }
}