namespace Aio.Dss.Inflector.Svc;

using System.Text.Json;
using Azure.Iot.Operations.Mqtt.Session;
using Azure.Iot.Operations.Protocol;
using Azure.Iot.Operations.Services.StateStore;
using MQTTnet.Exceptions;

public class DssDataSink : IDataSink
{
    private readonly ILogger _logger;    
    private readonly MqttSessionClient _mqttSessionClient;
    private readonly ApplicationContext _applicationContext;
    private int _initialBackoffDelayInMilliseconds;
    private int _maxBackoffDelayInMilliseconds;

    public DssDataSink(
        ILogger logger,        
        MqttSessionClient mqttSessionClient,
        ApplicationContext applicationContext,
        int initialBackoffDelayInMilliseconds = 500,
        int maxBackoffDelayInMilliseconds = 10_000)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));        
        _mqttSessionClient = mqttSessionClient ?? throw new ArgumentNullException(nameof(mqttSessionClient));
        _applicationContext = applicationContext ?? throw new ArgumentNullException(nameof(applicationContext));
        _initialBackoffDelayInMilliseconds = initialBackoffDelayInMilliseconds;
        _maxBackoffDelayInMilliseconds = maxBackoffDelayInMilliseconds;
    }

    public async Task PushDataAsync(string key, JsonDocument data, CancellationToken stoppingToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(data);        

        // Publish data to the DSS store until successful.
        var successfulPublish = false;
        int backoff_delay_in_milliseconds = _initialBackoffDelayInMilliseconds;

        // Note: Add max retries to prevent infinite loop blocking threads in all data sources and sinks.
        while (!successfulPublish)
        {
            try
            {
                await using StateStoreClient stateStoreClient = new(_applicationContext, _mqttSessionClient);
                {
                    // Push the ingress hybrid message to the state store.                    
                    await stateStoreClient.SetAsync(key, JsonSerializer.Serialize(data), null, null, stoppingToken);

                    // Reset backoff delay on successful data processing.
                    backoff_delay_in_milliseconds = _initialBackoffDelayInMilliseconds;
                    successfulPublish = true;
                }
            }
            catch (MqttCommunicationException ex)
            {
                _logger.LogError(ex, "Error publishing data to DSS store, reconnecting...");

                await Task.Delay(backoff_delay_in_milliseconds);
                backoff_delay_in_milliseconds = (int)Math.Pow(backoff_delay_in_milliseconds, 1.02);

                // Limit backoff delay to _maxBackoffDelayInMilliseconds.
                backoff_delay_in_milliseconds = backoff_delay_in_milliseconds > _maxBackoffDelayInMilliseconds ? _maxBackoffDelayInMilliseconds : backoff_delay_in_milliseconds;

                await _mqttSessionClient.ReconnectAsync();
            }
        }        
    }
}