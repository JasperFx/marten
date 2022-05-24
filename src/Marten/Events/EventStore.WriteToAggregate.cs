using System;
using System.Threading;
using System.Threading.Tasks;

namespace Marten.Events
{
    internal partial class EventStore
    {
        public async Task WriteToAggregate<T>(Guid id, Action<IEventStream<T>> writing, CancellationToken cancellation = default) where T : class
        {
            var stream = await FetchForWriting<T>(id, cancellation).ConfigureAwait(false);
            writing(stream);
            await _session.SaveChangesAsync(cancellation).ConfigureAwait(false);
        }

        public async Task WriteToAggregate<T>(Guid id, Func<IEventStream<T>, Task> writing, CancellationToken cancellation = default) where T : class
        {
            var stream = await FetchForWriting<T>(id, cancellation).ConfigureAwait(false);
            await writing(stream).ConfigureAwait(false);
            await _session.SaveChangesAsync(cancellation).ConfigureAwait(false);
        }

        public async Task WriteToAggregate<T>(string id, Action<IEventStream<T>> writing, CancellationToken cancellation = default) where T : class
        {
            var stream = await FetchForWriting<T>(id, cancellation).ConfigureAwait(false);
            writing(stream);
            await _session.SaveChangesAsync(cancellation).ConfigureAwait(false);
        }

        public async Task WriteToAggregate<T>(string id, Func<IEventStream<T>, Task> writing, CancellationToken cancellation = default) where T : class
        {
            var stream = await FetchForWriting<T>(id, cancellation).ConfigureAwait(false);
            await writing(stream).ConfigureAwait(false);
            await _session.SaveChangesAsync(cancellation).ConfigureAwait(false);
        }

        public async Task WriteToAggregate<T>(Guid id, int initialVersion, Action<IEventStream<T>> writing, CancellationToken cancellation = default) where T : class
        {
            var stream = await FetchForWriting<T>(id, initialVersion, cancellation).ConfigureAwait(false);
            writing(stream);
            await _session.SaveChangesAsync(cancellation).ConfigureAwait(false);
        }

        public async Task WriteToAggregate<T>(Guid id, int initialVersion, Func<IEventStream<T>, Task> writing, CancellationToken cancellation = default) where T : class
        {
            var stream = await FetchForWriting<T>(id, initialVersion, cancellation).ConfigureAwait(false);
            await writing(stream).ConfigureAwait(false);
            await _session.SaveChangesAsync(cancellation).ConfigureAwait(false);
        }

        public async Task WriteToAggregate<T>(string id, int initialVersion, Action<IEventStream<T>> writing, CancellationToken cancellation = default) where T : class
        {
            var stream = await FetchForWriting<T>(id, initialVersion, cancellation).ConfigureAwait(false);
            writing(stream);
            await _session.SaveChangesAsync(cancellation).ConfigureAwait(false);
        }

        public async Task WriteToAggregate<T>(string id, int initialVersion, Func<IEventStream<T>, Task> writing, CancellationToken cancellation = default) where T : class
        {
            var stream = await FetchForWriting<T>(id, initialVersion, cancellation).ConfigureAwait(false);
            await writing(stream).ConfigureAwait(false);
            await _session.SaveChangesAsync(cancellation).ConfigureAwait(false);
        }

        public async Task WriteExclusivelyToAggregate<T>(Guid id, Action<IEventStream<T>> writing, CancellationToken cancellation = default) where T : class
        {
            var stream = await FetchForExclusiveWriting<T>(id, cancellation).ConfigureAwait(false);
            writing(stream);
            await _session.SaveChangesAsync(cancellation).ConfigureAwait(false);
        }

        public async Task WriteExclusivelyToAggregate<T>(string id, Action<IEventStream<T>> writing, CancellationToken cancellation = default) where T : class
        {
            var stream = await FetchForExclusiveWriting<T>(id, cancellation).ConfigureAwait(false);
            writing(stream);
            await _session.SaveChangesAsync(cancellation).ConfigureAwait(false);
        }

        public async Task WriteExclusivelyToAggregate<T>(Guid id, Func<IEventStream<T>, Task> writing, CancellationToken cancellation = default) where T : class
        {
            var stream = await FetchForExclusiveWriting<T>(id, cancellation).ConfigureAwait(false);
            await writing(stream).ConfigureAwait(false);
            await _session.SaveChangesAsync(cancellation).ConfigureAwait(false);
        }

        public async Task WriteExclusivelyToAggregate<T>(string id, Func<IEventStream<T>, Task> writing, CancellationToken cancellation = default) where T : class
        {
            var stream = await FetchForExclusiveWriting<T>(id, cancellation).ConfigureAwait(false);
            await writing(stream).ConfigureAwait(false);
            await _session.SaveChangesAsync(cancellation).ConfigureAwait(false);
        }
    }
}
