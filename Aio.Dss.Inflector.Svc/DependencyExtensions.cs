namespace Aio.Dss.Inflector.Svc;

using Aio.Dss.Inflector.Svc.BusinessLogic.CycleTimeAverage;
using Aio.Dss.Inflector.Svc.BusinessLogic.ShiftCounting;
using Azure.Iot.Operations.Protocol;
using Azure.Iot.Operations.Protocol.Telemetry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;

public static class DependencyExtensions
{
    private const string MqttReadClientIdPrefix = "MQTT-Read";
    private const string MqttWriteClientIdPrefix = "MQTT-Write";
    private const string DssReadClientIdPrefix = "DSS-Read";
    private const string DssWriteClientIdPrefix = "DSS-Write";

    public static IServiceCollection AddConfig(
         this IServiceCollection services, IConfiguration config)
    {
        if (config is null)
        {
            throw new ArgumentNullException(nameof(config));
        }

        services.Configure<MqttOptions>(
             config.GetSection(MqttOptions.Mqtt));

        services.Configure<MqttDataSinkOptions>(
             config.GetSection(MqttDataSinkOptions.MqttDataSink));

        services.Configure<MqttDataSourceOptions>(
             config.GetSection(MqttDataSourceOptions.MqttDataSource));

        services.Configure<DssDataSinkOptions>(
            config.GetSection(DssDataSinkOptions.DssDataSink));

        services.Configure<DssDataSourceOptions>(
            config.GetSection(DssDataSourceOptions.DssDataSource));

        return services;
    }

    public static IServiceCollection AddDependencies(
        this IServiceCollection services)
    {
        services.AddSingleton(provider =>
        {
            var mqttOptions = provider.GetRequiredService<IOptions<MqttOptions>>();
            return new MqttClientFactoryProvider(
                provider.GetRequiredService<ILogger<MqttClientFactoryProvider>>(),
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
            var mqttDataSourceOptions = provider.GetRequiredService<IOptions<MqttDataSourceOptions>>();
            var applicationContext = provider.GetRequiredService<ApplicationContext>();

            var sessionClient = CreateSessionClient(
                provider,
                $"{MqttReadClientIdPrefix}{mqttOptions.Value.ClientId}");

            var hybridMessageReceiver = new HybridMessageReceiver(applicationContext, sessionClient);
            hybridMessageReceiver.TopicNamespace = mqttDataSourceOptions.Value.TopicNamespace;

            return hybridMessageReceiver;
        });

        services.TryAddKeyedSingleton<IDataSource>(
            Constants.DssDataSourceKey,
            (provider, serviceKey) =>
            {
                var mqttOptions = provider.GetRequiredService<IOptions<MqttOptions>>();
                var dssDataSourceOptions = provider.GetRequiredService<IOptions<DssDataSourceOptions>>();
                var applicationContext = provider.GetRequiredService<ApplicationContext>();

                var dssSessionClient = CreateSessionClient(
                    provider,
                    $"{DssReadClientIdPrefix}{mqttOptions.Value.ClientId}");

                var resiliencePipeline = CreateResiliencePipeline<DssDataSource>(
                    provider,
                    dssDataSourceOptions.Value.MaxRetries,
                    TimeSpan.FromMilliseconds(dssDataSourceOptions.Value.MaxDelayInMilliseconds),
                    TimeSpan.FromMilliseconds(dssDataSourceOptions.Value.TimeoutInMilliseconds),
                    dssDataSourceOptions.Value.Jitter);

                return new DssDataSource(
                    provider.GetRequiredService<ILogger<DssDataSource>>(),
                    dssSessionClient,
                    applicationContext,
                    resiliencePipeline);
            });

        services.TryAddKeyedSingleton<IDataSink>(
            Constants.DssDataSinkKey,
            (provider, serviceKey) =>
            {
                var mqttOptions = provider.GetRequiredService<IOptions<MqttOptions>>();
                var dssDataSinkOptions = provider.GetRequiredService<IOptions<DssDataSinkOptions>>();
                var applicationContext = provider.GetRequiredService<ApplicationContext>();

                var dssSessionClient = CreateSessionClient(
                    provider,
                    $"{DssWriteClientIdPrefix}{mqttOptions.Value.ClientId}");

                var dssResiliencePipeline = CreateResiliencePipeline<DssDataSink>(
                    provider,
                    dssDataSinkOptions.Value.MaxRetries,
                    TimeSpan.FromMilliseconds(dssDataSinkOptions.Value.MaxDelayInMilliseconds),
                    TimeSpan.FromMilliseconds(dssDataSinkOptions.Value.TimeoutInMilliseconds),
                    dssDataSinkOptions.Value.Jitter);

                return new DssDataSink(
                    provider.GetRequiredService<ILogger<DssDataSink>>(),
                    dssSessionClient,
                    applicationContext,
                    dssResiliencePipeline);
            });

        services.TryAddKeyedSingleton<IDataSink>(
            Constants.MqttDataSinkKey,
            (provider, serviceKey) =>
            {
                var mqttOptions = provider.GetRequiredService<IOptions<MqttOptions>>();
                var mqttDataSinkOptions = provider.GetRequiredService<IOptions<MqttDataSinkOptions>>();
                var applicationContext = provider.GetRequiredService<ApplicationContext>();

                var mqttSessionClient = CreateSessionClient(
                    provider,
                    $"{MqttWriteClientIdPrefix}{mqttOptions.Value.ClientId}");

                var mqttResiliencePipeline = CreateResiliencePipeline<MqttDataSink>(
                    provider,
                    mqttDataSinkOptions.Value.MaxRetries,
                    TimeSpan.FromMilliseconds(mqttDataSinkOptions.Value.MaxDelayInMilliseconds),
                    TimeSpan.FromMilliseconds(mqttDataSinkOptions.Value.TimeoutInMilliseconds),
                    mqttDataSinkOptions.Value.Jitter);

                return new MqttDataSink(
                    provider.GetRequiredService<ILogger<MqttDataSink>>(),
                    mqttSessionClient,
                    mqttResiliencePipeline);
            });

        services.TryAddKeyedSingleton<string>(
            Constants.MqttEgressTopic,
            (provider, serviceKey) =>
                {
                    var mqttDataSinkOptions = provider.GetRequiredService<IOptions<MqttDataSinkOptions>>();
                    return mqttDataSinkOptions.Value.Topic;
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

    private static IMqttPubSubClient CreateSessionClient(IServiceProvider provider, string clientId)
    {
        var mqttOptions = provider.GetRequiredService<IOptions<MqttOptions>>().Value;

        var sessionClient = provider
            .GetRequiredService<MqttClientFactoryProvider>()
            .GetSessionClient(
                mqttOptions.Logging,
                clientId,
                mqttOptions.MaxRetries,
                mqttOptions.MaxDelayInMilliseconds,
                mqttOptions.Jitter,
                mqttOptions.ConnectionTimeoutInSMilliseconds)
            .GetAwaiter()
            .GetResult();

        return sessionClient;
    }

    private static ResiliencePipeline CreateResiliencePipeline<T>(
        IServiceProvider provider,
        int maxRetryAttempts,
        TimeSpan maxDelay,
        TimeSpan timeout,
        bool jitter)
    {
        return CreateResiliencePipeline(provider, typeof(T), maxRetryAttempts, maxDelay, timeout, jitter);
    }

    private static ResiliencePipeline CreateResiliencePipeline(
        IServiceProvider provider,
        Type loggerType,
        int maxRetryAttempts,
        TimeSpan maxDelay,
        TimeSpan timeout,
        bool jitter)
    {
        // Get a logger factory once rather than getting a logger for each retry
        var loggerFactory = provider.GetRequiredService<ILoggerFactory>();

        return new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions()
            {
                MaxRetryAttempts = maxRetryAttempts,
                UseJitter = jitter,
                MaxDelay = maxDelay,
                BackoffType = DelayBackoffType.Exponential,
                OnRetry = args =>
                {
                    // Create a logger from the factory - no need to dispose as ILoggerFactory handles this
                    var logger = loggerFactory.CreateLogger(loggerType);
                    logger.LogWarning(
                        "Polly is retrying operation. Attempt {attempt}. Exception: {exception}",
                        args.AttemptNumber,
                        args.Outcome.Exception);

                    return ValueTask.CompletedTask;
                }
            })
            .AddTimeout(timeout)
            .Build();
    }
}
