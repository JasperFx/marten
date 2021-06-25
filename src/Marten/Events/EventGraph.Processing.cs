using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events.Operations;
using Marten.Internal;
using Marten.Internal.Operations;
using Marten.Internal.Sessions;
using Marten.Schema.Identity;

namespace Marten.Events
{
    public partial class EventGraph
    {
        internal void ProcessEvents(DocumentSessionBase session)
        {
            if (!session.WorkTracker.Streams.Any())
            {
                return;
            }

            var storage = session.EventStorage();

            var fetcher = new EventSequenceFetcher(this, session.WorkTracker.Streams.Sum(x => x.Events.Count));
            var sequences = session.ExecuteHandler(fetcher);


            foreach (var stream in session.WorkTracker.Streams)
            {
                stream.TenantId ??= session.Tenant.TenantId;

                if (stream.ActionType == StreamActionType.Start)
                {
                    stream.PrepareEvents(0, this, sequences, session);
                    session.QueueOperation(storage.InsertStream(stream));
                }
                else
                {
                    var handler = storage.QueryForStream(stream);
                    var state = session.ExecuteHandler(handler);

                    if (state == null)
                    {
                        stream.PrepareEvents(0, this, sequences, session);
                        session.QueueOperation(storage.InsertStream(stream));
                    }
                    else
                    {
                        stream.PrepareEvents(state.Version, this, sequences, session);
                        session.QueueOperation(storage.UpdateStreamVersion(stream));
                    }
                }

                foreach (var @event in stream.Events)
                {
                    session.QueueOperation(storage.AppendEvent(this, session, stream, @event));
                }
            }

            foreach (var projection in _inlineProjections.Value)
            {
                projection.Apply(session, session.WorkTracker.Streams.ToList());
            }
        }

        internal async Task ProcessEventsAsync(DocumentSessionBase session, CancellationToken token)
        {
            if (!session._workTracker.Streams.Any())
            {
                return;
            }

            var fetcher = new EventSequenceFetcher(this, session.WorkTracker.Streams.Sum(x => x.Events.Count));
            var sequences = await session.ExecuteHandlerAsync(fetcher, token);


            var storage = session.EventStorage();

            foreach (var stream in session.WorkTracker.Streams)
            {
                stream.TenantId ??= session.Tenant.TenantId;

                if (stream.ActionType == StreamActionType.Start)
                {
                    stream.PrepareEvents(0, this, sequences, session);
                    session.QueueOperation(storage.InsertStream(stream));
                }
                else
                {
                    var handler = storage.QueryForStream(stream);
                    var state = await session.ExecuteHandlerAsync(handler, token);

                    if (state == null)
                    {
                        stream.PrepareEvents(0, this, sequences, session);
                        session.QueueOperation(storage.InsertStream(stream));
                    }
                    else
                    {
                        stream.PrepareEvents(state.Version, this, sequences, session);
                        session.QueueOperation(storage.UpdateStreamVersion(stream));
                    }
                }

                foreach (var @event in stream.Events)
                {
                    session.QueueOperation(storage.AppendEvent(this, session, stream, @event));
                }
            }

            foreach (var projection in _inlineProjections.Value)
            {
                await projection.ApplyAsync(session, session.WorkTracker.Streams.ToList(), token);
            }
        }

        internal bool TryCreateTombstoneBatch(DocumentSessionBase session, out UpdateBatch batch)
        {
            if (session.WorkTracker.Streams.Any())
            {
                var stream = StreamAction.ForTombstone();

                var tombstone = new Tombstone();
                var mapping = EventMappingFor<Tombstone>();

                var operations = new List<IStorageOperation>();
                var storage = session.EventStorage();

                operations.Add(_establishTombstone.Value);
                var tombstones = session.WorkTracker.Streams
                    .SelectMany(x => x.Events)
                    .Select(x => new Event<Tombstone>(tombstone)
                    {
                        Sequence = x.Sequence,
                        Version = x.Version,
                        TenantId = x.TenantId,
                        StreamId = EstablishTombstoneStream.StreamId,
                        StreamKey = EstablishTombstoneStream.StreamKey,
                        Id = CombGuidIdGeneration.NewGuid(),
                        EventTypeName = mapping.EventTypeName,
                        DotNetTypeName = mapping.DotNetTypeName
                    })
                    .Select(e => storage.AppendEvent(this, session, stream, e));

                operations.AddRange(tombstones);

                batch = new UpdateBatch(operations);

                return true;
            }

            batch = null;
            return false;
        }

    }
}
