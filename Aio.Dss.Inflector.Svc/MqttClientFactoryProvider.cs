namespace Aio.Dss.Inflector.Svc;

using Azure.Iot.Operations.Mqtt.Session;
using Azure.Iot.Operations.Protocol.Connection;

public class SessionClientFactory
{
    private readonly ILogger _logger;
    private readonly string _host;
    private readonly int _port;
    private readonly bool _useTls;
    private readonly string _username;
    private readonly string _satFilePath;
    private readonly string _caFilePath;
    private readonly string _passwordFilePath;

    public SessionClientFactory(ILogger<SessionClientFactory> logger, string host, int port, bool useTls, string username, string satFilePath, string caFilePath, string passwordFilePath)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _port = port;
        _useTls = useTls;
        _username = username ?? throw new ArgumentNullException(nameof(username));
        _satFilePath = satFilePath;
        _caFilePath = caFilePath;
        _passwordFilePath = passwordFilePath;
    }

    public async Task<MqttSessionClient> GetSessionClient(bool MqttLogging, string clientIdExtension)
    {
        _logger.LogInformation("MQTT SAT token file location: '{file}'.", _satFilePath);
        _logger.LogInformation("CA cert file location: '{file}'.", _caFilePath);
        _logger.LogInformation("Password file location: '{file}'.", _passwordFilePath);

        // To be discussed: 
        // QoS1 setting for subscribers with TelemetryReceiver - review
        // CleanStart= is set to true by default - we want to set false when subscribing with QoS1 so not to lose any messages upon restart/rolling update

        MqttConnectionSettings connectionSettings = new(_host)
        {
            TcpPort = _port,            
            ClientId = "AIO-DSS-Inflector-" + clientIdExtension,
            UseTls = _useTls,
            Username = _username,
            PasswordFile = _passwordFilePath,
            SatAuthFile = _satFilePath,
            CaFile = _caFilePath
        };

        _logger.LogInformation("Connecting to: {settings}", connectionSettings);
        
        MqttSessionClient sessionClient = new(new MqttSessionClientOptions() { EnableMqttLogging = MqttLogging });
        await sessionClient.ConnectAsync(connectionSettings);

        return sessionClient;
    }
}
