namespace Aio.Dss.Inflector.Svc;

using Azure.Iot.Operations.Protocol;
using Azure.Iot.Operations.Protocol.Telemetry;

[TelemetryTopic("aio-dss-inflector/data/ingresstest")]
public class HybridMessageReceiver : TelemetryReceiver<IngressHybridMessage>
{
    internal HybridMessageReceiver(IMqttPubSubClient mqttClient)
        : base(mqttClient, "HybridMessageReceiver", new Utf8JsonSerializer())
    {
    }
}