using Marten;

namespace EventAppenderPerfTester;

public interface ITestPlan
{
    Task Execute(IDocumentSession session);
    void FetchData();
}