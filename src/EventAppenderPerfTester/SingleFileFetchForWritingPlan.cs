using DaemonTests.TestingSupport;
using Marten;

namespace EventAppenderPerfTester;

public class SingleFileFetchForWritingPlan: ITestPlan
{
    private List<TripStream> _data;

    public async Task Execute(IDocumentSession session)
    {
        foreach (var tripStream in _data)
        {
            await tripStream.PublishSingleFileWithFetchForWriting(session);
        }
    }

    public void FetchData()
    {
        _data = TripStreamReaderWriter.ReadPages(1);
    }
}