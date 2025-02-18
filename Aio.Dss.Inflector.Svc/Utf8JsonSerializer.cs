namespace Aio.Dss.Inflector.Svc;

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Iot.Operations.Protocol;
using Azure.Iot.Operations.Protocol.Models;

public class EmptyJson
{
}

public class Utf8JsonSerializer : IPayloadSerializer
{
    protected static readonly JsonSerializerOptions jsonSerializerOptions = new()
    {        
    };

    public const string ContentType = "application/json";

    public const MqttPayloadFormatIndicator PayloadFormatIndicator = MqttPayloadFormatIndicator.CharacterData;


    SerializedPayloadContext IPayloadSerializer.ToBytes<T>(T? payload) where T : class
    {
        try
        {
            var serializedBytes = JsonSerializer.SerializeToUtf8Bytes(payload, jsonSerializerOptions);
            return new SerializedPayloadContext(serializedBytes, ContentType, PayloadFormatIndicator);
        }
        catch (Exception)
        {
            throw AkriMqttException.GetPayloadInvalidException();
        }
    }

    public T FromBytes<T>(byte[]? payload, string? contentType, MqttPayloadFormatIndicator payloadFormatIndicator) where T : class
    {
        if (payload == null || payload.Length == 0)
        {
            if (typeof(T) != typeof(EmptyJson))
            {
                throw AkriMqttException.GetPayloadInvalidException();
            }

            return (new EmptyJson() as T)!;
        }

        // Console.WriteLine($"Received payload: {Encoding.UTF8.GetString(payload)}");
        // var str = "{ \"action\": \"SomeAction\", \"actionData\": \"SomeActionData\", \"payload\": \"SomePayload\" }";
        Utf8JsonReader reader = new(payload);        

        return JsonSerializer.Deserialize<T>(ref reader, jsonSerializerOptions)!;
        // return default;
    }
}