using System.Threading.Tasks;
using Marten.Testing.Documents;

namespace Marten.Testing.Linq.Compatibility.Support
{
    public abstract class LinqTestCase
    {
        public string Description { get; set; }

        public abstract Task Compare(IQuerySession session, Target[] documents);

        public bool Ordered { get; set; }
    }
}
