namespace Aio.Dss.Inflector.Svc;

using Azure.Iot.Operations.Protocol;
using Azure.Iot.Operations.Protocol.Telemetry;

[TelemetryTopic("aio-dss-inflector/data/ingress")]
public class HybridMessageReceiver : TelemetryReceiver<IngressHybridMessage>
{
    internal HybridMessageReceiver(ApplicationContext applicationContext, IMqttPubSubClient mqttClient)
        : base(applicationContext, mqttClient, "HybridMessageReceiver", new Utf8JsonSerializer())
    {
    }
}