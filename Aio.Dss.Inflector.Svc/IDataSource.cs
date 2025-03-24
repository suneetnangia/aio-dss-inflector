namespace Aio.Dss.Inflector.Svc;

using System.Text.Json;

public interface IDataSource
{
    Task<JsonDocument> ReadDataAsync(string key, CancellationToken stoppingToken);
}
