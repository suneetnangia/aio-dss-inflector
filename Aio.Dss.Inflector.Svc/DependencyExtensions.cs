namespace Aio.Dss.Inflector.Svc;

using Azure.Iot.Operations.Protocol;
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

        services.AddSingleton<ApplicationContext>();

        services.AddSingleton<TelemetryReceiver<IngressHybridMessage>>(provider =>
        {
            var mqtt_options = provider.GetRequiredService<IOptions<MqttOptions>>();
            var applicationContext = provider.GetRequiredService<ApplicationContext>();
            var sessionClient = provider.GetRequiredService<SessionClientFactory>().GetSessionClient(mqtt_options.Value.Logging, $"AioDssInflectorMQTTRead{mqtt_options.Value.ClientId}").GetAwaiter().GetResult();
            return new HybridMessageReceiver(applicationContext, sessionClient);
        });

        services.AddSingleton<Dictionary<string, IDataSource>>(provider =>
        {
            var mqtt_options = provider.GetRequiredService<IOptions<MqttOptions>>();
            var dssSessionClient = provider.GetRequiredService<SessionClientFactory>().GetSessionClient(mqtt_options.Value.Logging, $"AioDssInflectorDSSRead{mqtt_options.Value.ClientId}").GetAwaiter().GetResult();
            var applicationContext = provider.GetRequiredService<ApplicationContext>();
            var dssDataSink = new DssDataSource(
                provider.GetRequiredService<ILogger<DssDataSource>>(),
                dssSessionClient,
                applicationContext);

            return new Dictionary<string, IDataSource>
            {
                { "DssDataSource", dssDataSink },              
            };
        });

        services.AddSingleton<Dictionary<string, IDataSink>>(provider =>
        {
            var mqtt_options = provider.GetRequiredService<IOptions<MqttOptions>>();
            var dssSessionClient = provider.GetRequiredService<SessionClientFactory>().GetSessionClient(mqtt_options.Value.Logging, $"AioDssInflectorDSSWrite{mqtt_options.Value.ClientId}").GetAwaiter().GetResult();
            var applicationContext = provider.GetRequiredService<ApplicationContext>();
            var dssDataSink = new DssDataSink(
                provider.GetRequiredService<ILogger<DssDataSink>>(),
                dssSessionClient,
                applicationContext);

            var mqttSessionClient = provider.GetRequiredService<SessionClientFactory>().GetSessionClient(mqtt_options.Value.Logging, $"AioDssInflectorMQTTWrite{mqtt_options.Value.ClientId}").GetAwaiter().GetResult();
            var mqttDataSink = new MqttDataSink(
                provider.GetRequiredService<ILogger<MqttDataSink>>(),
                mqttSessionClient, 
                "aio-dss-inflector/data/egress");

            return new Dictionary<string, IDataSink>
            {
                { "DssDataSink", dssDataSink },
                { "MqttDataSink", mqttDataSink }
            };
        });

        services.AddSingleton<Dictionary<string, IInflectorActionLogic>>(provider =>
        {
            // Note: this could be refactored to inject only one of the logics based on configuration, so the logic is dedicated to one action per deployment
            // Keeping it simple to processing two actions within same project and single deployment for now
            return new Dictionary<string, IInflectorActionLogic>
            {
                { "CycleTimeAverage", new CycleTimeAverageLogic(provider.GetRequiredService<ILogger<CycleTimeAverageLogic>>()) },
                { "ShiftCounter", new TotalCounterLogic(provider.GetRequiredService<ILogger<TotalCounterLogic>>()) }
            };
        });

        return services;
    }
}