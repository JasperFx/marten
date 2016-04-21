using System.Threading;
using System.Threading.Tasks;

namespace Marten.Events.Projections
{
    public interface IProjection
    {
        void Apply(IDocumentSession session);
        Task ApplyAsync(IDocumentSession session, CancellationToken token);
    }
}