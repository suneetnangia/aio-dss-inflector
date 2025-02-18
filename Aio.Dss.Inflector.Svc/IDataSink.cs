namespace Aio.Dss.Inflector.Svc;

using System.Text.Json;

public interface IDataSink
{
    Task PushDataAsync(string key, JsonDocument data, CancellationToken stoppingToken);
}