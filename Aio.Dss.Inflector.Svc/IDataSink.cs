namespace Aio.Dss.Inflector.Svc;

using System.Text.Json;

public interface IDataSink : IAsyncDisposable
{
    Task PushDataAsync(string key, JsonDocument data, CancellationToken stoppingToken);
}
