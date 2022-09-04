using System.Threading.Tasks;
using Marten;
using Marten.Testing.Documents;

namespace DocumentDbTests.Reading.Linq.Compatibility.Support;

public abstract class LinqTestCase
{
    public string Description { get; set; }

    public abstract Task Compare(IQuerySession session, Target[] documents);

    public bool Ordered { get; set; }
}