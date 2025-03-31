namespace Aio.Dss.Inflector.Svc;

using System;
using System.Buffers;
using System.Text.Json;
using Azure.Iot.Operations.Protocol;
using Azure.Iot.Operations.Protocol.Models;

public class Utf8JsonSerializer : IPayloadSerializer
{
    public const string ContentType = "application/json";
    public const MqttPayloadFormatIndicator PayloadFormatIndicator = MqttPayloadFormatIndicator.CharacterData;

    private static readonly JsonSerializerOptions DefaultOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public SerializedPayloadContext ToBytes<T>(T? payload)
        where T : class
    {
        try
        {
            // Handle null payload
            if (payload == null)
            {
                return new(new(JsonSerializer.SerializeToUtf8Bytes(new { })), ContentType, PayloadFormatIndicator);
            }

            // Serialize all other objects normally
            return new(new(JsonSerializer.SerializeToUtf8Bytes(payload, DefaultOptions)), ContentType, PayloadFormatIndicator);
        }
        catch
        {
            throw AkriMqttException.GetPayloadInvalidException();
        }
    }

    public T FromBytes<T>(ReadOnlySequence<byte> payload, string? contentType, MqttPayloadFormatIndicator payloadFormatIndicator)
        where T : class
    {
        if (contentType != null && contentType != ContentType)
        {
            throw new AkriMqttException($"Content type {contentType} is not supported by this implementation; only {ContentType} is accepted.")
            {
                Kind = AkriMqttErrorKind.HeaderInvalid,
                HeaderName = "Content Type",
                HeaderValue = contentType,
                InApplication = false,
                IsShallow = false,
                IsRemote = false,
            };
        }

        try
        {
            if (payload.IsEmpty)
            {
                // For empty payloads, try creating a default instance or deserializing from empty JSON
                return DeserializeEmptyPayload<T>();
            }

            Utf8JsonReader reader = new(payload);
            T? result = JsonSerializer.Deserialize<T>(ref reader, DefaultOptions);

            if (result == null)
            {
                throw AkriMqttException.GetPayloadInvalidException();
            }

            return result;
        }
        catch (Exception ex) when (!(ex is AkriMqttException))
        {
            throw AkriMqttException.GetPayloadInvalidException();
        }
    }

    private static T DeserializeEmptyPayload<T>()
        where T : class
    {
        try
        {
            // Try to create instance using parameterless constructor
            return Activator.CreateInstance<T>() ?? throw new InvalidOperationException("Failed to create instance of type");
        }
        catch
        {
            try
            {
                // If that fails, try to deserialize from empty JSON object
                return JsonSerializer.Deserialize<T>("{}", DefaultOptions)
                    ?? throw new InvalidOperationException("Failed to deserialize empty JSON");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Cannot create instance of {typeof(T).Name} from empty payload", ex);
            }
        }
    }
}
