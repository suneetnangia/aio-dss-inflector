# Makefile for Aio.Dss.Inflector MQTT operations

# Variables
SERVICE_CMD = dotnet run --project Aio.Dss.Inflector.Svc
MQTT_BROKER = localhost
MQTT_PORT = 1883
DEFAULT_INGRESS_TOPIC = aio-dss-inflector/data/ingress
DEFAULT_EGRESS_TOPIC = aio-dss-inflector/data/egress
DEFAULT_MESSAGE = '{ "correlationId": "e8e1d420-a070-4673-89a6-c23744388ee4",  "action": 1, "actionRequestDataPayload":  {"CycleTime": { "SourceTimestamp": "2025-02-19T14:34:38.3250966Z", "Value": 28 }}, "passthroughPayload": { "keyA": "valueA", "keyB": "valueB" }}'

# Help command
.PHONY: help
help:
	@echo "Available commands:"
	@echo "  make run-service    - Run the Aio.Dss.Inflector.Svc"
	@echo "  make pub            - Publish default message to MQTT"
	@echo "  make pub-custom     - Publish custom message (use TOPIC=... MSG=...)"
	@echo "  make sub            - Subscribe to default MQTT topic"	

# Run the service
.PHONY: run-service
run-service:
	$(SERVICE_CMD)

# Publish default message to MQTT
.PHONY: pub
pub:
	mosquitto_pub -q 1 -h $(MQTT_BROKER) -p $(MQTT_PORT) -t $(DEFAULT_INGRESS_TOPIC) -m $(DEFAULT_MESSAGE)

# Publish custom message to MQTT
.PHONY: pub-custom
pub-custom:
	@if [ -z "$(TOPIC)" ]; then echo "Error: TOPIC is required"; exit 1; fi
	@if [ -z "$(MSG)" ]; then echo "Error: MSG is required"; exit 1; fi
	mosquitto_pub -q 1 -h $(MQTT_BROKER) -p $(MQTT_PORT) -t $(TOPIC) -m '$(MSG)'

# Subscribe to default MQTT topic
.PHONY: sub
sub:
	mosquitto_sub -q 1 -h $(MQTT_BROKER) -p $(MQTT_PORT) -t $(DEFAULT_EGRESS_TOPIC) -v
