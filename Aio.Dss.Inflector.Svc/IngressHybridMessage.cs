namespace Aio.Dss.Inflector.Svc;

using System.Text.Json;
using System.Text.Json.Serialization;

public class IngressHybridMessage
{
    [JsonPropertyName("correlationId")]
    public required string CorrelationId { get; set; }

    [JsonPropertyName("action")]
    public required InflectorAction Action { get; set; }

    [JsonPropertyName("actionRequestDataPayload")]
    public required JsonDocument ActionRequestDataPayload { get; set; }

    [JsonPropertyName("passthroughPayload")]
    public required JsonDocument? PassthroughPayload { get; set; }
}

// Sample JSON when this class is deserialized
/*
{
    "correlationId": "12345",
    "action": 1,
    "actionRequestDataPayload": {
        "key1": "value1",
        "key2": "value2"
    },
    "passthroughPayload": {
        "keyA": "valueA",
        "keyB": "valueB"
    }
}
*/
