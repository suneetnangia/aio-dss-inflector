// This file is used by Code Analysis to maintain SuppressMessage
// attributes that are applied to this project.
// Project-level suppressions either have no target or are given
// a specific target and scoped to a namespace, type, member, etc.

using System.Diagnostics.CodeAnalysis;

[assembly: SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Disposed in Worker class", Scope = "member", Target = "~M:Aio.Dss.Inflector.Svc.DependencyExtensions.CreateSessionClient(System.IServiceProvider,System.String)~Azure.Iot.Operations.Protocol.IMqttPubSubClient")]
