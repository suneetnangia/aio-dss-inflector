namespace Aio.Dss.Inflector.Svc;

using Aio.Dss.Inflector.Svc.BusinessLogic.CycleTimeAverage;
using Aio.Dss.Inflector.Svc.BusinessLogic.ShiftCounter;
using Azure.Iot.Operations.Protocol;
using Azure.Iot.Operations.Protocol.Telemetry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

public static class DependencyExtensions
{
    private const string MQTT_READ_CLIENT_ID_PREFIX = "MQTT-Read";
    private const string MQTT_WRITE_CLIENT_ID_PREFIX = "MQTT-Write";
    private const string DSS_READ_CLIENT_ID_PREFIX = "DSS-Read";
    private const string DSS_WRITE_CLIENT_ID_PREFIX = "DSS-Write";    

    public static IServiceCollection AddConfig(
         this IServiceCollection services, IConfiguration config)
    {
        if (config is null)
        {
            throw new ArgumentNullException(nameof(config));
        }

        services.Configure<MqttOptions>(
             config.GetSection(MqttOptions.Mqtt));

        return services;
    }

    public static IServiceCollection AddDependencies(
        this IServiceCollection services)
    {
        services.AddSingleton(provider =>
        {
            var mqttOptions = provider.GetRequiredService<IOptions<MqttOptions>>();
            return new SessionClientFactory(
                provider.GetRequiredService<ILogger<SessionClientFactory>>(),
                mqttOptions.Value.Host,
                mqttOptions.Value.Port,
                mqttOptions.Value.UseTls,
                mqttOptions.Value.Username,
                mqttOptions.Value.SatFilePath,
                mqttOptions.Value.CaFilePath,
                mqttOptions.Value.PasswordFilePath);
        });

        services.AddSingleton<ApplicationContext>();

        services.AddSingleton<TelemetryReceiver<IngressHybridMessage>>(provider =>
        {
            var mqttOptions = provider.GetRequiredService<IOptions<MqttOptions>>();
            var applicationContext = provider.GetRequiredService<ApplicationContext>();
            var sessionClient = provider
                .GetRequiredService<SessionClientFactory>()
                .GetSessionClient(mqttOptions.Value.Logging, $"{MQTT_READ_CLIENT_ID_PREFIX}{mqttOptions.Value.ClientId}")
                .GetAwaiter()
                .GetResult();

            return new HybridMessageReceiver(applicationContext, sessionClient);
        });

        services.AddSingleton(provider =>
        {
            var mqttOptions = provider.GetRequiredService<IOptions<MqttOptions>>();
            var dssSessionClient = provider
                .GetRequiredService<SessionClientFactory>()
                .GetSessionClient(mqttOptions.Value.Logging, $"{DSS_READ_CLIENT_ID_PREFIX}{mqttOptions.Value.ClientId}")
                .GetAwaiter()
                .GetResult();

            var applicationContext = provider.GetRequiredService<ApplicationContext>();
            var dssDataSink = new DssDataSource(
                provider.GetRequiredService<ILogger<DssDataSource>>(),
                dssSessionClient,
                applicationContext);

            return new Dictionary<string, IDataSource>
            {
                { Constants.DSS_DATA_SOURCE_KEY, dssDataSink },
            };
        });

        services.AddSingleton(provider =>
        {
            var mqttOptions = provider.GetRequiredService<IOptions<MqttOptions>>();
            var dssSessionClient = provider
                .GetRequiredService<SessionClientFactory>()
                .GetSessionClient(mqttOptions.Value.Logging, $"{DSS_WRITE_CLIENT_ID_PREFIX}{mqttOptions.Value.ClientId}")
                .GetAwaiter()
                .GetResult();

            var applicationContext = provider.GetRequiredService<ApplicationContext>();
            var dssDataSink = new DssDataSink(
                provider.GetRequiredService<ILogger<DssDataSink>>(),
                dssSessionClient,
                applicationContext);

            var mqttSessionClient = provider
                .GetRequiredService<SessionClientFactory>()
                .GetSessionClient(mqttOptions.Value.Logging, $"{MQTT_WRITE_CLIENT_ID_PREFIX}{mqttOptions.Value.ClientId}")
                .GetAwaiter()
                .GetResult();

            var mqttDataSink = new MqttDataSink(
                provider.GetRequiredService<ILogger<MqttDataSink>>(),
                mqttSessionClient);

            return new Dictionary<string, IDataSink>
            {
                { Constants.DSS_DATA_SINK_KEY, dssDataSink },
                { Constants.MQTT_DATA_SINK_KEY, mqttDataSink }
            };
        });

        services.AddSingleton(provider =>
        {
            return new Dictionary<InflectorAction, IInflectorActionLogic>
            {
                { InflectorAction.CycleTimeAverage, new CycleTimeAverageLogic(provider.GetRequiredService<ILogger<CycleTimeAverageLogic>>()) },
                { InflectorAction.ShiftCounter, new TotalCounterLogic(provider.GetRequiredService<ILogger<TotalCounterLogic>>()) }
            };
        });

        return services;
    }
}