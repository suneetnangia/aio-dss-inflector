namespace Aio.Dss.Inflector.Svc;

// https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration/options?view=aspnetcore-8.0#options-validation
public class MqttDataSourceOptions
{
    public const string MqttDataSource = "MqttDataSource";

    /// <summary>
    /// Topic namespace for the MQTT data source i.e topic is formed by {TopicNamespace}/{endpoint001}
    //  where 'endpoint001' is configured in HybridMessageReceiver class.
    /// </summary>
    public required string TopicNamespace { get; set; } = "aio-dss-inflector/data/ingress";
}