namespace Aio.Dss.Inflector.Svc;

// https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration/options?view=aspnetcore-8.0#options-validation
public class MqttOptions
{
    public const string Mqtt = "Mqtt";
    public required bool Logging { get; set; }
    public required string Host { get; set; }
    public int Port { get; set; } = 1883;
    public bool UseTls { get; set; } = false;
    public required string Username { get; set; }
    public required string PasswordFilePath { get; set; }
    public required string SatFilePath { get; set; }
    public required string CaFilePath { get; set; }
    public required string ClientId { get; set; }
}