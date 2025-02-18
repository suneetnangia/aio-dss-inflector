namespace Aio.Dss.Inflector.Svc;

using System.Text.Json;
using Azure.Iot.Operations.Mqtt.Session;
using Azure.Iot.Operations.Services.StateStore;
using MQTTnet.Exceptions;

public class DssDataSource : IDataSource
{
    private readonly ILogger _logger;
    private readonly MqttSessionClient _mqttSessionClient;
    private int _initialBackoffDelayInMilliseconds;
    private int _maxBackoffDelayInMilliseconds;
    private int _maxRetires;

    public DssDataSource(
        ILogger logger,
        MqttSessionClient mqttSessionClient,
        int initialBackoffDelayInMilliseconds = 500,
        int maxBackoffDelayInMilliseconds = 10_000,
        int maxRetires = 3) 
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _mqttSessionClient = mqttSessionClient ?? throw new ArgumentNullException(nameof(mqttSessionClient));
        _initialBackoffDelayInMilliseconds = initialBackoffDelayInMilliseconds;
        _maxBackoffDelayInMilliseconds = maxBackoffDelayInMilliseconds;
        _maxRetires = maxRetires;
    }

    public async Task<JsonDocument> ReadDataAsync(string key, CancellationToken stoppingToken)
    {
        ArgumentNullException.ThrowIfNull(key);

        // Read data from the DSS store until successful.        
        var successfulRead = false;        
        int backoff_delay_in_milliseconds = _initialBackoffDelayInMilliseconds;
        
        while (!successfulRead)
        {        
            try
            {
                await using StateStoreClient stateStoreClient = new(_mqttSessionClient);
                {
                    // Read the data for the provided key in the state store.
                    var dssResponse = await stateStoreClient.GetAsync(key, null, stoppingToken);
                    _logger.LogTrace($"Read data from DSS store for key: '{key}', returned version '{dssResponse.Version}', data '{dssResponse.Value}'.");

                    // Reset backoff delay on successful data processing.
                    backoff_delay_in_milliseconds = _initialBackoffDelayInMilliseconds;
                    successfulRead = true;

                    return JsonDocument.Parse(dssResponse.Value?.Bytes);
                }
            }
            catch (MqttCommunicationException ex)
            {
                _logger.LogError(ex, "Error reading data from DSS store, reconnecting...");

                await Task.Delay(backoff_delay_in_milliseconds);
                backoff_delay_in_milliseconds = (int)Math.Pow(backoff_delay_in_milliseconds, 1.02);

                // Limit backoff delay to _maxBackoffDelayInMilliseconds.
                backoff_delay_in_milliseconds = backoff_delay_in_milliseconds > _maxBackoffDelayInMilliseconds ? _maxBackoffDelayInMilliseconds : backoff_delay_in_milliseconds;

                await _mqttSessionClient.ReconnectAsync();
            }
        }

        return null;      
    }
}