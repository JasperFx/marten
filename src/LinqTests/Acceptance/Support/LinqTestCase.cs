using System.Threading.Tasks;
using Marten;
using Marten.Testing.Documents;
using Marten.Testing.Harness;

namespace LinqTests.Acceptance.Support;

public abstract class LinqTestCase
{
    public string Description { get; set; }

    public abstract Task Compare(IQuerySession session, Target[] documents,
        TestOutputMartenLogger logger);

    public bool Ordered { get; set; }
}
