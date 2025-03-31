# Makefile for Aio.Dss.Inflector MQTT operations

# Variables
SERVICE_CMD = dotnet run --project Aio.Dss.Inflector.Svc
MQTT_BROKER = localhost
MQTT_PORT = 1883
DEFAULT_INGRESS_TOPIC = aio-dss-inflector/data/ingress/endpoint001
DEFAULT_EGRESS_TOPIC = aio-dss-inflector/data/egress/endpoint001
DOCKER_IMAGE_NAME = aio-dss-inflector
DOCKER_TAG = latest

# Help command
.PHONY: help
help:
	@echo "Available commands:"
	@echo "  make set-state	 	 - Sets reference data in the state store"
	@echo "  make run-service    - Run the Aio.Dss.Inflector.Svc"
	@echo "  make pub            - Publish default message to MQTT"
	@echo "  make pub-custom     - Publish custom message (use TOPIC=... MSG=...)"
	@echo "  make sub            - Subscribe to default MQTT topic"
	@echo "  make docker-build   - Build Docker image (use TAG=... to override tag)"

# Run the service
.PHONY: run-service
run-service: set-state
	$(SERVICE_CMD)

# Set a value in the state store
.PHONY: set-state
set-state:
	./tools/statestore-cli set -n localhost -p 1883 -k "shifts" -f "./Aio.Dss.Inflector.Svc/BusinessLogic/samplemessages/dss-reference-data.json" --notls

# Publish default message to MQTT
.PHONY: pub
pub:
	mosquitto_pub -q 1 -h $(MQTT_BROKER) -p $(MQTT_PORT) -t $(DEFAULT_INGRESS_TOPIC) -f "./Aio.Dss.Inflector.Svc/BusinessLogic/samplemessages/ingress-cycletime-1.json"

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

# Build Docker image
.PHONY: docker-build
docker-build:
	@echo "Building Docker image $(DOCKER_IMAGE_NAME):$(if $(TAG),$(TAG),$(DOCKER_TAG))"
	docker build --progress=plain -t $(DOCKER_IMAGE_NAME):$(if $(TAG),$(TAG),$(DOCKER_TAG)) .
