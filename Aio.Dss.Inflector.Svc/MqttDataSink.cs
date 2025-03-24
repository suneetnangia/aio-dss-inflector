namespace Aio.Dss.Inflector.Svc;

using System.Text;
using System.Text.Json;
using Azure.Iot.Operations.Mqtt.Session;
using Azure.Iot.Operations.Protocol.Models;
using MQTTnet.Exceptions;
using Polly;
using Polly.Retry;

public class MqttDataSink : IDataSink
{
    private readonly ILogger _logger;    
    private readonly MqttSessionClient _mqttSessionClient;
    private readonly int _initialBackoffDelayInMilliseconds;
    private readonly int _maxBackoffDelayInMilliseconds;
    private readonly int _maxRetryAttempts;
    private readonly AsyncRetryPolicy _retryPolicy;

    public MqttDataSink(
        ILogger logger,        
        MqttSessionClient mqttSessionClient,
        int initialBackoffDelayInMilliseconds = 500,
        int maxBackoffDelayInMilliseconds = 10_000,
        int maxRetryAttempts = 10)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));        
        _mqttSessionClient = mqttSessionClient ?? throw new ArgumentNullException(nameof(mqttSessionClient));
        _initialBackoffDelayInMilliseconds = initialBackoffDelayInMilliseconds;
        _maxBackoffDelayInMilliseconds = maxBackoffDelayInMilliseconds;
        _maxRetryAttempts = maxRetryAttempts;
        
        // Configure Polly retry policy once during initialization
        // TODO: Create a base class and move this to base class
        _retryPolicy = Policy
            .Handle<MqttCommunicationException>()
            .WaitAndRetryAsync(
                retryCount: _maxRetryAttempts,
                sleepDurationProvider: (retryAttempt, context) => 
                {
                    // Calculate exponential backoff with jitter
                    var exponentialBackoff = TimeSpan.FromMilliseconds(
                        Math.Min(
                            _initialBackoffDelayInMilliseconds * Math.Pow(1.5, retryAttempt),
                            _maxBackoffDelayInMilliseconds
                        )
                    );
                    
                    // Add small random jitter to prevent multiple clients retrying simultaneously
                    return exponentialBackoff + TimeSpan.FromMilliseconds(new Random().Next(0, 100));
                },
                onRetryAsync: async (exception, timeSpan, retryCount, context) => 
                {
                    _logger.LogError(exception, "Error publishing data to MQTT broker, topic: '{Topic}', attempt {RetryCount}, retrying after {RetryDelay}ms...", 
                        context["topic"], retryCount, timeSpan.TotalMilliseconds);
                    
                    await _mqttSessionClient.ReconnectAsync();
                }
            );
    }

    public async Task PushDataAsync(string key, JsonDocument data, CancellationToken stoppingToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(data);

        var mqtt_application_message = new MqttApplicationMessage(key, MqttQualityOfServiceLevel.AtLeastOnce)
        {
            PayloadSegment = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(data)),
            // Note: add standardized user properties based on cloud event extensions in AIO.
        };

        var context = new Context
        {
            ["topic"] = key
        };

        await _retryPolicy.ExecuteAsync(async (ctx) => 
        {
            await _mqttSessionClient.PublishAsync(mqtt_application_message, stoppingToken);
            _logger.LogTrace("Published data to MQTT broker, topic: '{topic}'.", key);
        }, context);
    }
}