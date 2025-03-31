namespace Aio.Dss.Inflector.Svc;

using System.Text.Json;
using System.Text.Json.Serialization;

public class EgressHybridMessage
{
    [JsonPropertyName("correlationId")]
    public required string CorrelationId { get; set; }

    [JsonPropertyName("actionResponseDataPayload")]
    public required JsonDocument ActionResponseDataPayload { get; set; }

    [JsonPropertyName("passthroughPayload")]
    public required JsonDocument? PassthroughPayload { get; set; }
}

// Sample JSON when this class is deserialized
/*
{
    "correlationId": "12345",
    "actionResponseDataPayload": {
        "key1": "value1",
        "key2": "value2"
    },
    "passthroughPayload": {
        "key3": "value3"
    }
}
*/
