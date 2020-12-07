using System.Threading;
using System.Threading.Tasks;
using Marten.Events.V4Concept.Aggregation;

namespace Marten.Events.V4Concept
{
    public interface IAsyncProjection
    {
        Task<IV4EventPage> Fetch(long floor, long ceiling, CancellationToken token);
    }
}