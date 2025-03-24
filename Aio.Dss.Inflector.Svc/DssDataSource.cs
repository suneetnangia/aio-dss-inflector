namespace Aio.Dss.Inflector.Svc;

using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Iot.Operations.Mqtt.Session;
using Azure.Iot.Operations.Protocol;
using Azure.Iot.Operations.Services.StateStore;
using MQTTnet.Exceptions;
using Polly;
using Polly.Retry;

public class DssDataSource : IDataSource
{
    private readonly ILogger _logger;
    private readonly MqttSessionClient _mqttSessionClient;
    private readonly ApplicationContext _applicationContext;
    private readonly int _initialBackoffDelayInMilliseconds;
    private readonly int _maxBackoffDelayInMilliseconds;
    private readonly int _maxRetryAttempts;
    private readonly AsyncRetryPolicy _retryPolicy;

    public DssDataSource(
        ILogger logger,
        MqttSessionClient mqttSessionClient,
        ApplicationContext applicationContext,
        int initialBackoffDelayInMilliseconds = 500,
        int maxBackoffDelayInMilliseconds = 10_000,
        int maxRetryAttempts = 10) 
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _mqttSessionClient = mqttSessionClient ?? throw new ArgumentNullException(nameof(mqttSessionClient));
        _applicationContext = applicationContext ?? throw new ArgumentNullException(nameof(applicationContext));
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
                    _logger.LogError(exception, "Error reading data from DSS store for key: '{Key}' (attempt {RetryCount}), retrying after {RetryDelay}ms...", 
                        context["key"], retryCount, timeSpan.TotalMilliseconds);
                    
                    await _mqttSessionClient.ReconnectAsync();
                }
            );
    }

    // Note - Evaluate if we want to change the signature of the interface to return string/bytes instead of JsonDocument
    public async Task<JsonDocument> ReadDataAsync(string key, CancellationToken stoppingToken)
    {
        ArgumentNullException.ThrowIfNull(key);

        var context = new Context
        {
            ["key"] = key
        };

        return await _retryPolicy.ExecuteAsync(async (ctx) => 
        {
            await using StateStoreClient stateStoreClient = new(_applicationContext, _mqttSessionClient);
            
            // Read the data for the provided key in the state store.
            var dssResponse = await stateStoreClient.GetAsync(key, null, stoppingToken);
            _logger.LogTrace("Read data from DSS store for key: '{Key}', returned version '{Version}', data '{Value}'.",
                key, dssResponse.Version, dssResponse.Value);

            if (dssResponse.Value == null)
            {
                _logger.LogWarning("No data found in DSS for key: '{Key}'.", key);
                return JsonDocument.Parse("{}");
            }
            
            // Process the data from the DSS response
            var data = dssResponse.Value?.Bytes;
            if (data != null)
            {
                var dataString = System.Text.Encoding.UTF8.GetString(data);

                // Note currently supporting JsonDocument or JSON Lines per DSS reference format for data flows
                if (dataString.Contains(Environment.NewLine))
                {
                    var jsonArray = new JsonArray();
                    foreach (var line in dataString.Split(Environment.NewLine))
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
                // Evaluate if we want to throw an exception or return an empty document
                // Key not found is not an error, could be first use
                return JsonDocument.Parse("{}");
            }
        }, context);
    }
}