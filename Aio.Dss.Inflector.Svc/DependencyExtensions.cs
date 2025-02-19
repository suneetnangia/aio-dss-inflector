namespace Aio.Dss.Inflector.Svc;

using Azure.Iot.Operations.Protocol.Telemetry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

public static class DependencyExtensions
{
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
        services.AddSingleton<SessionClientFactory>(provider =>
        {
            var mqtt_options = provider.GetRequiredService<IOptions<MqttOptions>>();
            return new SessionClientFactory(
                provider.GetRequiredService<ILogger<SessionClientFactory>>(),
                mqtt_options.Value.Host,
                mqtt_options.Value.Port,
                mqtt_options.Value.UseTls,
                mqtt_options.Value.Username,
                mqtt_options.Value.SatFilePath,
                mqtt_options.Value.CaFilePath,
                mqtt_options.Value.PasswordFilePath);
        });

        services.AddSingleton<TelemetryReceiver<IngressHybridMessage>>(provider =>
        {
            var mqtt_options = provider.GetRequiredService<IOptions<MqttOptions>>();
            var sessionClient = provider.GetRequiredService<SessionClientFactory>().GetSessionClient(mqtt_options.Value.Logging, "AioDssInflectorMQTTRead").GetAwaiter().GetResult();
            return new HybridMessageReceiver(sessionClient);
        });

        services.AddSingleton<Dictionary<string, IDataSource>>(provider =>
        {
            var mqtt_options = provider.GetRequiredService<IOptions<MqttOptions>>();
            var dssSessionClient = provider.GetRequiredService<SessionClientFactory>().GetSessionClient(mqtt_options.Value.Logging, "AioDssInflectorDSSRead").GetAwaiter().GetResult();
            var dssDataSink = new DssDataSource(
                provider.GetRequiredService<ILogger<DssDataSource>>(),
                dssSessionClient);

            return new Dictionary<string, IDataSource>
            {
                { "DssDataSource", dssDataSink },              
            };
        });

        services.AddSingleton<Dictionary<string, IDataSink>>(provider =>
        {
            var mqtt_options = provider.GetRequiredService<IOptions<MqttOptions>>();
            var dssSessionClient = provider.GetRequiredService<SessionClientFactory>().GetSessionClient(mqtt_options.Value.Logging, "AioDssInflectorDSSWrite").GetAwaiter().GetResult();
            var dssDataSink = new DssDataSink(
                provider.GetRequiredService<ILogger<DssDataSink>>(),
                dssSessionClient);

            var mqttSessionClient = provider.GetRequiredService<SessionClientFactory>().GetSessionClient(mqtt_options.Value.Logging, "AioDssInflectorMQTTWrite").GetAwaiter().GetResult();
            var mqttDataSink = new MqttDataSink(
                provider.GetRequiredService<ILogger<MqttDataSink>>(),
                mqttSessionClient, "aio-dss-inflector/data/egress");

            return new Dictionary<string, IDataSink>
            {
                { "DssDataSink", dssDataSink },
                { "MqttDataSink", mqttDataSink }
            };
        });

        services.AddSingleton<Dictionary<string, IInflectorActionLogic>>(provider =>
        {
            return new Dictionary<string, IInflectorActionLogic>
            {
                { "CycleTimeAverage", new ShiftCycleAverageLogic(provider.GetRequiredService<ILogger<ShiftCycleAverageLogic>>()) },
                { "ShiftCounter", new TotalCounterLogic(provider.GetRequiredService<ILogger<TotalCounterLogic>>()) }
            };
        });

        return services;
    }
}