using DaemonTests.TestingSupport;
using Marten;

namespace EventAppenderPerfTester;

public class MultiplesTestPlan: ITestPlan
{
    private List<TripStream[]> _data;

    public async Task Execute(IDocumentSession session)
    {
        foreach (var trips in _data)
        {
            await TripStream.PublishMultiplesSimple(session, trips);
        }
    }

    public void FetchData()
    {
        _data = TripStreamReaderWriter.ReadPages(100, 10);
    }
}