namespace Aio.Dss.Inflector.Svc;

using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Iot.Operations.Mqtt.Session;
using Azure.Iot.Operations.Protocol;
using Azure.Iot.Operations.Services.StateStore;
using MQTTnet.Exceptions;

public class DssDataSource : IDataSource
{
    private readonly ILogger _logger;
    private readonly MqttSessionClient _mqttSessionClient;
    private readonly ApplicationContext _applicationContext;
    private int _initialBackoffDelayInMilliseconds;
    private int _maxBackoffDelayInMilliseconds;
    private int _maxRetires;

    public DssDataSource(
        ILogger logger,
        MqttSessionClient mqttSessionClient,
        ApplicationContext applicationContext,
        int initialBackoffDelayInMilliseconds = 500,
        int maxBackoffDelayInMilliseconds = 10_000,
        int maxRetires = 3) 
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _mqttSessionClient = mqttSessionClient ?? throw new ArgumentNullException(nameof(mqttSessionClient));
        _applicationContext = applicationContext ?? throw new ArgumentNullException(nameof(applicationContext));
        _initialBackoffDelayInMilliseconds = initialBackoffDelayInMilliseconds;
        _maxBackoffDelayInMilliseconds = maxBackoffDelayInMilliseconds;
        _maxRetires = maxRetires;
    }

    // Note - Evaluate if we want to change the signature of the interface to return string/bytes instead of JsonDocument
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
                await using StateStoreClient stateStoreClient = new(_applicationContext, _mqttSessionClient);
                {
                    // Read the data for the provided key in the state store.
                    var dssResponse = await stateStoreClient.GetAsync(key, null, stoppingToken);
                    _logger.LogTrace($"Read data from DSS store for key: '{key}', returned version '{dssResponse.Version}', data '{dssResponse.Value}'.");

                    // Reset backoff delay on successful data processing.
                    backoff_delay_in_milliseconds = _initialBackoffDelayInMilliseconds;
                    successfulRead = true;

                    if (dssResponse.Value == null)
                    {
                        _logger.LogWarning($"No data found in DSS for key: '{key}'.");
                        return JsonDocument.Parse("{}");
                    }
                    else
                    {

                        // also support JSON Lines - discuss if we want the source to know about the format and content type
                        // we could generalize and leave decoding to the caller  
                        var data = dssResponse.Value?.Bytes;
                        if (data != null)
                        {
                            var dataString = System.Text.Encoding.UTF8.GetString(data);
                            // Note currently supporting JsonDocument or JSON Lines per DSS reference format for data flows
                            if (dataString.Contains("\n"))
                            {
                                var jsonArray = new JsonArray();
                                foreach (var line in dataString.Split('\n'))
                                {
                                    if (!string.IsNullOrWhiteSpace(line))
                                    {
                                        jsonArray.Add(JsonDocument.Parse(line).RootElement.Clone());
                                    }
                                }
                                return JsonDocument.Parse(jsonArray.ToString());
                            }
                            else
                            {
                                // Handle single JSON document
                                return JsonDocument.Parse(data);
                            }
                        }
                        else
                        {
                            // Evaluate if we want to throw an exception or return an empty document, key not found is not an error, could be first use
                            return JsonDocument.Parse("{}");
                        }
                    }
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

        throw new InvalidOperationException("Failed to read data from DSS store after multiple retries.");      
    }
}