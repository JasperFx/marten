using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LamarCodeGeneration;
using Marten.Internal;

namespace Marten.Events.V4Concept.Aggregation
{
    public abstract class V4AggregateProjection<T>: IAggregateProjection
    {
        private GeneratedType _liveType;
        private GeneratedType _inlineType;
        private GeneratedType _asyncDaemonType;

        public IList<Type> DeleteEvents { get; } = new List<Type>();
        public IList<Type> CreateEvents { get; } = new List<Type>();

        Type IAggregateProjection.AggregateType => typeof(T);

        GeneratedType IAggregateProjection.LiveAggregationType
        {
            get => _liveType;
            set => _liveType = value;
        }

        GeneratedType IAggregateProjection.InlineType
        {
            get => _inlineType;
            set => _inlineType = value;
        }

        GeneratedType IAggregateProjection.AsyncAggregationType
        {
            get => _asyncDaemonType;
            set => _asyncDaemonType = value;
        }

        public bool WillDelete(IEnumerable<IEvent> events)
        {
            return events.Select(x => x.EventType).Intersect(DeleteEvents).Any();
        }


        internal ILiveAggregator<T> BuildLiveAggregator()
        {
            return (ILiveAggregator<T>)Activator.CreateInstance(_liveType.CompiledType, this);
        }

        internal IInlineProjection BuildInlineProjection(IMartenSession session)
        {
            var storage = session.StorageFor<T>();

            return (IInlineProjection)Activator.CreateInstance(_inlineType.CompiledType, GetType().Name, storage, this);
        }


        internal string SourceCode()
        {
            var writer = new StringWriter();
            writer.WriteLine(_liveType.SourceCode);
            writer.WriteLine();

            writer.WriteLine(_inlineType.SourceCode);
            writer.WriteLine();

            return writer.ToString();
        }
    }
}
