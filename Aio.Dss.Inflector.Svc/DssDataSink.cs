namespace Aio.Dss.Inflector.Svc;

using System.Text.Json;
using Azure.Iot.Operations.Protocol;
using Azure.Iot.Operations.Services.StateStore;
using Polly;

public class DssDataSink : IDataSink
{
    private readonly ILogger _logger;
    private readonly IMqttPubSubClient _mqttSessionClient;
    private readonly ApplicationContext _applicationContext;
    private readonly ResiliencePipeline _resiliencePipeline;

    public DssDataSink(
        ILogger logger,
        IMqttPubSubClient mqttSessionClient,
        ApplicationContext applicationContext,
        ResiliencePipeline resiliencePipeline)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _mqttSessionClient = mqttSessionClient ?? throw new ArgumentNullException(nameof(mqttSessionClient));
        _applicationContext = applicationContext ?? throw new ArgumentNullException(nameof(applicationContext));
        _resiliencePipeline = resiliencePipeline ?? throw new ArgumentNullException(nameof(resiliencePipeline));       
    }

    public async Task PushDataAsync(string key, JsonDocument data, CancellationToken stoppingToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(stoppingToken);

        await _resiliencePipeline.ExecuteAsync(async cancellationToken =>
        {
            await using StateStoreClient stateStoreClient = new(_applicationContext, _mqttSessionClient);

            // Push the ingress hybrid message to the state store.                    
            await stateStoreClient.SetAsync(key, JsonSerializer.Serialize(data), null, null, cancellationToken);
        }, stoppingToken);
    }
}