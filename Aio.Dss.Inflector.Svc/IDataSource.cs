namespace Aio.Dss.Inflector.Svc;

using System.Text.Json;

public interface IDataSource : IAsyncDisposable
{
    Task<JsonDocument> ReadDataAsync(string key, CancellationToken stoppingToken);
}
