using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten.Events.Projections;
#nullable enable
namespace Marten.Events.Aggregation
{
    public partial class AggregateProjection<T>: IAggregateProjection
    {
        internal IList<Type> DeleteEvents { get; } = new List<Type>();
        internal IList<Type> TransformedEvents { get; } = new List<Type>();

        public AggregateProjection<T> CreateEvent<TEvent>(Func<TEvent, T> creator) where TEvent : class
        {
            _createMethods.AddLambda(creator, typeof(TEvent));
            return this;
        }

        public AggregateProjection<T> CreateEvent<TEvent>(Func<TEvent, IQuerySession, Task<T>> creator) where TEvent : class
        {
            _createMethods.AddLambda(creator, typeof(TEvent));
            return this;
        }

        public AggregateProjection<T> DeleteEvent<TEvent>() where TEvent : class
        {
            DeleteEvents.Add(typeof(TEvent));
            return this;
        }

        public AggregateProjection<T> DeleteEvent<TEvent>(Func<TEvent, bool> shouldDelete) where TEvent : class
        {
            _shouldDeleteMethods.AddLambda(shouldDelete, typeof(TEvent));
            return this;
        }

        public AggregateProjection<T> DeleteEvent<TEvent>(Func<T, TEvent, bool> shouldDelete) where TEvent : class
        {
            _shouldDeleteMethods.AddLambda(shouldDelete, typeof(TEvent));
            return this;
        }


        public AggregateProjection<T> DeleteEventAsync<TEvent>(
            Func<IQuerySession, T, TEvent, Task<bool>> shouldDelete) where TEvent : class
        {
            _shouldDeleteMethods.AddLambda(shouldDelete, typeof(TEvent));
            return this;
        }

        public AggregateProjection<T> ProjectEvent<TEvent>(Action<T> handler)
            where TEvent : class
        {
            _applyMethods.AddLambda(handler, typeof(TEvent));
            return this;
        }

        public AggregateProjection<T> ProjectEvent<TEvent>(Action<T, TEvent> handler)
            where TEvent : class
        {
            _applyMethods.AddLambda(handler, typeof(TEvent));
            return this;
        }

        public AggregateProjection<T> ProjectEvent<TEvent>(Func<T, TEvent, T> handler)
            where TEvent : class
        {
            _applyMethods.AddLambda(handler, typeof(TEvent));
            return this;
        }

        public AggregateProjection<T> ProjectEvent<TEvent>(Func<T, T> handler)
            where TEvent : class
        {
            _applyMethods.AddLambda(handler, typeof(TEvent));
            return this;
        }

        public AggregateProjection<T> ProjectEvent<TEvent>(Action<IQuerySession, T, TEvent> handler)
            where TEvent : class
        {
            _applyMethods.AddLambda(handler, typeof(TEvent));
            return this;
        }

        public AggregateProjection<T> ProjectEventAsync<TEvent>(Func<IQuerySession, T, TEvent, Task> handler)
        {
            _applyMethods.AddLambda(handler, typeof(TEvent));
            return this;
        }

        public AggregateProjection<T> TransformsEvent<TEvent>() where TEvent : class
        {
            TransformedEvents.Add(typeof(TEvent));
            return this;
        }

        public bool MatchesAnyDeleteType(IEventSlice slice)
        {
            return slice.Events().Select(x => x.EventType).Intersect(DeleteEvents).Any();
        }

        public bool MatchesAnyDeleteType(StreamAction action)
        {
            return action.Events.Select(x => x.EventType).Intersect(DeleteEvents).Any();
        }

        protected override IEnumerable<Type> publishedTypes()
        {
            yield return typeof(T);
        }
    }
}
