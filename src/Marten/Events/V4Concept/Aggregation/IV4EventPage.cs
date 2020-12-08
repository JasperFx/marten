using System;
using System.Threading;
using System.Threading.Tasks;
using Marten.Internal;

namespace Marten.Events.V4Concept.Aggregation
{
    public interface IV4EventPage : IDisposable
    {
        // This will track the IDocumentSession? Yes. Also the resolve?

        public long Floor { get;}
        public long Ceiling { get; }

        Task<IUpdateBatch> ApplyChanges(CancellationToken cancellation);

        int Count { get; }
    }
}
