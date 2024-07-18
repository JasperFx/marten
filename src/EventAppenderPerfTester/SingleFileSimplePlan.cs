using DaemonTests.TestingSupport;
using Marten;

namespace EventAppenderPerfTester;

public class SingleFileSimplePlan: ITestPlan
{
    private List<TripStream> _data;

    public async Task Execute(IDocumentSession session)
    {
        foreach (var tripStream in _data)
        {
            await tripStream.PublishSingleFileSimple(session);
        }
    }

    public void FetchData()
    {
        _data = TripStreamReaderWriter.ReadPages(1);
    }
}