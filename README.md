# AIO DSS Inflector

The primary purpose of this service is to allow AIO's Distributed Data Store (DSS) be part of Data Flow for both read and write purposes, by virtue of that it's addressing the current gap in AIO to perform complex and stateful message processing.
It works on the messaging concept of pub-sub and lean towards building smaller services (nano) on top of Dataflow, as depicted below:

![AIO DSS Inflector](docs/media/aio-dss-inflector.png)

## Deployment (to be completed with docker build/helm deploy)

Sample `appsettings.json` file:

```json
{
  "Mqtt": {
    "Logging": true,
    "Host": "aio-broker",
    "Port": 18883,
    "UseTls": true,
    "Username": "",
    "Password": "",
    "SatFilePath": "/var/run/secrets/tokens/broker-sat",
    "CaFilePath": "/var/run/certs/ca.crt",
    "ClientId": "Aio.Dss.Inflector.Svc"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.Hosting.Lifetime": "Information"
    }
  }
}
```
