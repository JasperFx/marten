using System;
using System.Threading;
using System.Threading.Tasks;
using Marten.Internal;

namespace Marten.Events.V4Concept.Aggregation
{
    public interface IAsyncBatch : IDisposable
    {
        public long Floor { get;}
        public long Ceiling { get; }

        int Count { get; }

        Task<IUpdateBatch> Complete(CancellationToken cancellation);
    }

    public interface IStreamingAsyncBatch: IAsyncBatch
    {
        void Enqueue(IEvent @event);
    }
}
