namespace Aio.Dss.Inflector.Svc;

using System.Text.Json;
using Azure.Iot.Operations.Mqtt.Session;
using Azure.Iot.Operations.Protocol;
using Azure.Iot.Operations.Services.StateStore;
using MQTTnet.Exceptions;
using Polly;
using Polly.Retry;

public class DssDataSink : IDataSink
{
    private readonly ILogger _logger;    
    private readonly MqttSessionClient _mqttSessionClient;
    private readonly ApplicationContext _applicationContext;
    private readonly int _initialBackoffDelayInMilliseconds;
    private readonly int _maxBackoffDelayInMilliseconds;
    private readonly int _maxRetryAttempts;
    private readonly AsyncRetryPolicy _retryPolicy;

    public DssDataSink(
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
                    _logger.LogError(exception, "Error publishing data to DSS store (attempt {RetryCount}), retrying after {RetryDelay}ms...", 
                        retryCount, timeSpan.TotalMilliseconds);
                    
                    await _mqttSessionClient.ReconnectAsync();
                }
            );
    }

    public async Task PushDataAsync(string key, JsonDocument data, CancellationToken stoppingToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(data);        

        await _retryPolicy.ExecuteAsync(async () => 
        {
            await using StateStoreClient stateStoreClient = new(_applicationContext, _mqttSessionClient);
            
            // Push the ingress hybrid message to the state store.                    
            await stateStoreClient.SetAsync(key, JsonSerializer.Serialize(data), null, null, stoppingToken);
        });
    }
}