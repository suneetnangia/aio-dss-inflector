using Aio.Dss.Inflector.Svc;

public class TotalCounterLogic : IInflectorActionLogic
{
    // private readonly int _totalCounter = 0;

    private readonly ILogger<TotalCounterLogic> _logger;
    // private readonly string _dssKeyShiftsReference = "shifts";

    public TotalCounterLogic(ILogger<TotalCounterLogic> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<EgressHybridMessage> Execute(IngressHybridMessage message, IDataSource dataSource, IDataSink dataSink, CancellationToken cancellationToken)
    {
        // Logic to ensure the same message is not added twice to the 10 items array
        // Use query into the DSS array to check before pop oldest and push newest
        // throw not implemented exception
        throw new NotImplementedException();
    }
}