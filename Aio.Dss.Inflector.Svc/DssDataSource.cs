namespace Aio.Dss.Inflector.Svc;

using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Iot.Operations.Protocol;
using Azure.Iot.Operations.Services.StateStore;
using Polly;

public class DssDataSource : IDataSource
{
    private readonly ILogger _logger;
    private readonly IMqttPubSubClient _mqttSessionClient;
    private readonly ApplicationContext _applicationContext;
    private readonly ResiliencePipeline _resiliencePipeline;

    public DssDataSource(
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

    // Note - Evaluate if we want to change the signature of the interface to return string/bytes instead of JsonDocument
    public async Task<JsonDocument> ReadDataAsync(string key, CancellationToken stoppingToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(stoppingToken);
      
        return await _resiliencePipeline.ExecuteAsync<JsonDocument>(async cancellationToken =>
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
        }, stoppingToken);
    }
}