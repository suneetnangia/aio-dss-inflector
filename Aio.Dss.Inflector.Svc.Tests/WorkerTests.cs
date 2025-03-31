namespace Aio.Dss.Inflector.Svc.Tests;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.Iot.Operations.Protocol;
using Azure.Iot.Operations.Protocol.Telemetry;
using Microsoft.Extensions.Logging;
using Moq;

public class WorkerTests
{
    private const string MQTT_EGRESS_TOPIC = "aio-dss-inflector/data/egress";
    private readonly Mock<ILogger<Worker>> _mockLogger;
    private readonly Mock<TelemetryReceiver<IngressHybridMessage>> _mockTelemetryReceiver;
    private readonly Mock<IDataSource> _mockDssDataSource;
    private readonly Mock<IDataSink> _mockDssDataSink;
    private readonly Mock<IDataSink> _mockMqttDataSink;
    private readonly Mock<IInflectorActionLogic> _mockActionLogic;

    private readonly CancellationTokenSource _cts;
    private readonly Worker _worker;

    public WorkerTests()
    {
        _mockLogger = new Mock<ILogger<Worker>>();

        // Create mocks for TelemetryReceiver dependencies
        var mockApplicationContext = new Mock<ApplicationContext>(MockBehavior.Loose, (HybridLogicalClock)null);
        var mockMqttClient = new Mock<IMqttPubSubClient>();
        var mockSerializer = new Mock<IPayloadSerializer>();

        _mockTelemetryReceiver = new Mock<TelemetryReceiver<IngressHybridMessage>>(
            mockApplicationContext.Object,
            mockMqttClient.Object,
            "TestTelemetry",
            mockSerializer.Object
        );
        _mockDssDataSource = new Mock<IDataSource>();
        _mockDssDataSink = new Mock<IDataSink>();
        _mockMqttDataSink = new Mock<IDataSink>();
        _mockActionLogic = new Mock<IInflectorActionLogic>();

        // Set up the action dictionary with the mock action logic
        var actionLogicDictionary = new Dictionary<InflectorAction, IInflectorActionLogic>
        {
            { InflectorAction.CycleTimeAverage, _mockActionLogic.Object }
        };

        // Create the worker instance with mocked dependencies
        _worker = new Worker(
            _mockLogger.Object,
            _mockTelemetryReceiver.Object,
            _mockDssDataSource.Object,
            _mockMqttDataSink.Object,
            _mockDssDataSink.Object,
            MQTT_EGRESS_TOPIC,
            actionLogicDictionary);

        // Create field for the _ingressHybridMessages collection using reflection
        // This is needed because it's a private field in the Worker class
        var fieldInfo = typeof(Worker).GetField("_ingressHybridMessages",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        fieldInfo?.SetValue(_worker, new BlockingCollection<IngressHybridMessage>());

        _cts = new CancellationTokenSource();
    }

    [Fact]
    public async Task ProcessMessages_WithKnownAction_ExecutesActionLogicAndPublishesToMqtt()
    {
        // Arrange
        var ingressMessage = new IngressHybridMessage
        {
            Action = InflectorAction.CycleTimeAverage,
            CorrelationId = Guid.NewGuid().ToString(),
            PassthroughPayload = JsonDocument.Parse("{\"key\":\"test-key\"}"),
            ActionRequestDataPayload = JsonDocument.Parse("{\"key\":\"test-key\"}")
        };

        var egressMessage = new EgressHybridMessage
        {
            CorrelationId = ingressMessage.CorrelationId,
            PassthroughPayload = ingressMessage.PassthroughPayload,
            ActionResponseDataPayload = JsonDocument.Parse("{\"result\":\"success\"}")
        };

        _mockActionLogic
            .Setup(a => a.Execute(
                It.IsAny<IngressHybridMessage>(),
                It.IsAny<IDataSource>(),
                It.IsAny<IDataSink>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(egressMessage);

        // Get access to the blocking collection
        var fieldInfo = typeof(Worker).GetField("_ingressHybridMessages",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var collection = fieldInfo?.GetValue(_worker) as BlockingCollection<IngressHybridMessage>;

        // Add the message to the collection
        collection?.Add(ingressMessage);

        // Set up a task with the token that will be cancelled after a short delay
        _cts.CancelAfter(TimeSpan.FromMilliseconds(100));

        // Act
        try
        {
            await _worker.ProcessMessages(_cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected exception when the token is cancelled
        }

        // Assert
        _mockActionLogic.Verify(a => a.Execute(
            It.Is<IngressHybridMessage>(m => m.Action == InflectorAction.CycleTimeAverage),
            _mockDssDataSource.Object,
            _mockDssDataSink.Object,
            It.IsAny<CancellationToken>()),
            Times.Once);

        var options = new JsonSerializerOptions();
        _mockMqttDataSink.Verify(s => s.PushDataAsync(
            MQTT_EGRESS_TOPIC,
            It.Is<JsonDocument>(doc => JsonSerializer.Serialize(doc, options) == JsonSerializer.Serialize(egressMessage, options)),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessMessages_WithUnknownAction_PublishesOriginalMessageToMqtt()
    {
        // Arrange
        var ingressMessage = new IngressHybridMessage
        {
            Action = InflectorAction.ShiftCounter, // An action not in the configured action dictionary
            CorrelationId = Guid.NewGuid().ToString(),
            PassthroughPayload = JsonDocument.Parse("{\"key\":\"test-key\"}"),
            ActionRequestDataPayload = JsonDocument.Parse("{\"key\":\"test-key\"}")
        };

        // Get access to the blocking collection
        var fieldInfo = typeof(Worker).GetField("_ingressHybridMessages",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var collection = fieldInfo?.GetValue(_worker) as BlockingCollection<IngressHybridMessage>;

        // Add the message to the collection
        collection?.Add(ingressMessage);

        // Set up a task with the token that will be cancelled after a short delay
        _cts.CancelAfter(TimeSpan.FromMilliseconds(100));

        // Act
        try
        {
            await _worker.ProcessMessages(_cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected exception when the token is cancelled
        }

        // Assert
        _mockActionLogic.Verify(a => a.Execute(
            It.IsAny<IngressHybridMessage>(),
            It.IsAny<IDataSource>(),
            It.IsAny<IDataSink>(),
            It.IsAny<CancellationToken>()),
            Times.Never);

        var options = new JsonSerializerOptions();
        _mockMqttDataSink.Verify(s => s.PushDataAsync(
            MQTT_EGRESS_TOPIC,
            It.Is<JsonDocument>(doc => JsonSerializer.Serialize(doc, options) == JsonSerializer.Serialize(ingressMessage, options)),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }
}