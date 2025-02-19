using Aio.Dss.Inflector.Svc;

public interface IInflectorActionLogic
{
    Task<EgressHybridMessage> Execute(IngressHybridMessage message,  IDataSource dataSource, IDataSink dataSink, CancellationToken cancellationToken);
}