namespace Aio.Dss.Inflector.Svc;

using System.Text;
using System.Text.Json;
using Azure.Iot.Operations.Mqtt.Session;
using Azure.Iot.Operations.Protocol.Models;
using MQTTnet.Exceptions;

public class MqttDataSink : IDataSink
{
    private readonly ILogger _logger;    
    private readonly MqttSessionClient _mqttSessionClient;
    private readonly string _topic;
    private int _initialBackoffDelayInMilliseconds;
    private int _maxBackoffDelayInMilliseconds;

    public MqttDataSink(
        ILogger logger,        
        MqttSessionClient mqttSessionClient,
        string topic,
        int initialBackoffDelayInMilliseconds = 500,
        int maxBackoffDelayInMilliseconds = 10_000)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));        
        _mqttSessionClient = mqttSessionClient ?? throw new ArgumentNullException(nameof(mqttSessionClient));
        _topic = topic ?? throw new ArgumentNullException(nameof(topic));
        _initialBackoffDelayInMilliseconds = initialBackoffDelayInMilliseconds;
        _maxBackoffDelayInMilliseconds = maxBackoffDelayInMilliseconds;
    }

    public async Task PushDataAsync(string key, JsonDocument data, CancellationToken stoppingToken)
    {
        // Evaluate having the key be the destination topic, allow for multiple topics. Currently key is not used.
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(data);

        var mqtt_application_message = new MqttApplicationMessage(_topic, MqttQualityOfServiceLevel.AtLeastOnce)
        {
            PayloadSegment = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(data)),
            // Note: add standardized user properties based on cloud event extensions in AIO.
        };

        // Publish data to the MQTT broker until successful.
        var successfulPublish = false;
        int backoff_delay_in_milliseconds = _initialBackoffDelayInMilliseconds;
        while (!successfulPublish)
        {
            try
            {
                await _mqttSessionClient.PublishAsync(mqtt_application_message, stoppingToken);
                _logger.LogTrace("Published data to MQTT broker, topic: '{topic}'.", _topic);

                // Reset backoff delay on successful data processing.
                backoff_delay_in_milliseconds = _initialBackoffDelayInMilliseconds;
                successfulPublish = true;
            }
            catch (MqttCommunicationException ex)
            {
                _logger.LogError(ex, "Error publishing data to MQTT broker, topic: '{topic}', reconnecting...", _topic);

                await Task.Delay(backoff_delay_in_milliseconds);
                backoff_delay_in_milliseconds = (int)Math.Pow(backoff_delay_in_milliseconds, 1.02);

                // Limit backoff delay to _maxBackoffDelayInMilliseconds.
                backoff_delay_in_milliseconds = backoff_delay_in_milliseconds > _maxBackoffDelayInMilliseconds ? _maxBackoffDelayInMilliseconds : backoff_delay_in_milliseconds;

                await _mqttSessionClient.ReconnectAsync();
            }
        }       
    }
}