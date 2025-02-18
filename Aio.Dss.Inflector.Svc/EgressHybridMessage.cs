namespace Aio.Dss.Inflector.Svc;

using System.Text.Json;
using System.Text.Json.Serialization;

public class EgressHybridMessage
{
    [JsonPropertyName("correlationId")]
    required public string CorrelationId { get; set; }
    
    [JsonPropertyName("actionResponseDataPayload")]
    required public JsonDocument ActionResponseDataPayload { get; set; }

    [JsonPropertyName("passthroughPayload")]
    required public JsonDocument? PassthroughPayload { get; set; }
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