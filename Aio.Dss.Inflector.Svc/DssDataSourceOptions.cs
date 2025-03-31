namespace Aio.Dss.Inflector.Svc;

// https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration/options?view=aspnetcore-8.0#options-validation
public class DssDataSourceOptions
{   
    public const string DssDataSource = "DssDataSource";
    public required int MaxRetries { get; set; } = 3;
    public required double MaxDelayInMilliseconds { get; set; } = 1000 * 20;
    public required bool Jitter { get; set; } = true;
    public required double TimeoutInMilliseconds { get; set; } = 1000 * 20;
}