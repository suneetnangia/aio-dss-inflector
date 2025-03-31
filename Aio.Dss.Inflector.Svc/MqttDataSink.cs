namespace Aio.Dss.Inflector.Svc;

using System.Text;
using System.Text.Json;
using Azure.Iot.Operations.Protocol;
using Azure.Iot.Operations.Protocol.Models;
using Polly;

public class MqttDataSink : IDataSink
{
    private readonly ILogger _logger;
    private readonly IMqttPubSubClient _mqttSessionClient;
    private readonly ResiliencePipeline _resiliencePipeline;

    public MqttDataSink(
        ILogger logger,
        IMqttPubSubClient mqttSessionClient,
        ResiliencePipeline resiliencePipeline)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _mqttSessionClient = mqttSessionClient ?? throw new ArgumentNullException(nameof(mqttSessionClient));
        _resiliencePipeline = resiliencePipeline ?? throw new ArgumentNullException(nameof(resiliencePipeline));
    }

    public async Task PushDataAsync(string key, JsonDocument data, CancellationToken stoppingToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(stoppingToken);

        var mqtt_application_message = new MqttApplicationMessage(key, MqttQualityOfServiceLevel.AtLeastOnce)
        {
            PayloadSegment = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(data)),
            // Note: add standardized user properties based on cloud event extensions in AIO.
        };

        await _resiliencePipeline.ExecuteAsync(async cancellationToken =>
        {
            await _mqttSessionClient.PublishAsync(mqtt_application_message, cancellationToken);
            _logger.LogTrace("Published data to MQTT broker, topic: '{topic}'.", key);
        }, stoppingToken);
    }
}