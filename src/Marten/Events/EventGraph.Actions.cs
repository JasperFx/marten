using System;
using System.Linq;
using Marten.Internal.Sessions;

namespace Marten.Events
{
    public partial class EventGraph
    {
        internal StreamAction Append(DocumentSessionBase session, Guid stream, params object[] events)
        {
            EnsureAsGuidStorage(session);

            if (stream == Guid.Empty)
                throw new ArgumentOutOfRangeException(nameof(stream), "Cannot use an empty Guid as the stream id");

            var wrapped = events.Select(BuildEvent).ToArray();

            if (session.WorkTracker.TryFindStream(stream, out var eventStream))
            {
                eventStream.AddEvents(wrapped);
            }
            else
            {
                eventStream = StreamAction.Append(stream, wrapped);
                session.WorkTracker.Streams.Add(eventStream);
            }

            return eventStream;
        }

        internal StreamAction Append(DocumentSessionBase session, string stream, params object[] events)
        {
            EnsureAsStringStorage(session);

            if (stream.IsEmpty())
                throw new ArgumentOutOfRangeException(nameof(stream), "The stream key cannot be null or empty");

            var wrapped = events.Select(BuildEvent).ToArray();

            if (session.WorkTracker.TryFindStream(stream, out var eventStream))
            {
                eventStream.AddEvents(wrapped);
            }
            else
            {
                eventStream = StreamAction.Append(stream, wrapped);
                session.WorkTracker.Streams.Add(eventStream);
            }

            return eventStream;
        }

        internal StreamAction StartStream(DocumentSessionBase session, Guid id, params object[] events)
        {
            EnsureAsGuidStorage(session);

            if (id == Guid.Empty)
                throw new ArgumentOutOfRangeException(nameof(id), "Cannot use an empty Guid as the stream id");


            var stream = StreamAction.Start(this, id, events);
            session.WorkTracker.Streams.Add(stream);

            return stream;
        }

        internal StreamAction StartStream(DocumentSessionBase session, string streamKey, params object[] events)
        {
            EnsureAsStringStorage(session);

            if (streamKey.IsEmpty())
                throw new ArgumentOutOfRangeException(nameof(streamKey), "The stream key cannot be null or empty");


            var stream = StreamAction.Start(this, streamKey, events);

            session.WorkTracker.Streams.Add(stream);

            return stream;
        }


    }
}
