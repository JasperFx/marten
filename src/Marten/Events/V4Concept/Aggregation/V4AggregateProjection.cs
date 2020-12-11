using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Marten.Events.V4Concept.Aggregation
{
    public abstract partial class V4AggregateProjection<T>: IAggregateProjection
    {
        internal IList<Type> DeleteEvents { get; } = new List<Type>();

        public V4AggregateProjection<T> DeleteEvent<TEvent>() where TEvent : class
        {
            DeleteEvents.Add(typeof(TEvent));
            return this;
        }

        public V4AggregateProjection<T> DeleteEvent<TEvent>(Func<T, TEvent, bool> shouldDelete) where TEvent : class
        {
            _shouldDeleteMethods.AddLambda(shouldDelete, typeof(TEvent));
            return this;
        }

        public V4AggregateProjection<T> DeleteEvent<TEvent>(Func<IQuerySession, T, TEvent, bool> shouldDelete)
            where TEvent : class
        {
            _shouldDeleteMethods.AddLambda(shouldDelete, typeof(TEvent));
            return this;
        }

        public V4AggregateProjection<T> DeleteEventAsync<TEvent>(Func<T, TEvent, Task<bool>> shouldDelete)
            where TEvent : class
        {
            _shouldDeleteMethods.AddLambda(shouldDelete, typeof(TEvent));
            return this;
        }

        public V4AggregateProjection<T> DeleteEventAsync<TEvent>(
            Func<IQuerySession, T, TEvent, Task<bool>> shouldDelete) where TEvent : class
        {
            _shouldDeleteMethods.AddLambda(shouldDelete, typeof(TEvent));
            return this;
        }

        /*
         * TODOs
         * 1. Add overloads for immutable aggregates on ProjectEvent
         * 2. Add overloads called "Apply" instead
         * 3. Add Lambda overloads for "Create"
         *
         *
         *
         */

        public V4AggregateProjection<T> ProjectEvent<TEvent>(Action<T, TEvent> handler)
            where TEvent : class
        {
            _applyMethods.AddLambda(handler, typeof(TEvent));
            return this;
        }

        public V4AggregateProjection<T> ProjectEvent<TEvent>(Action<IQuerySession, T, TEvent> handler)
            where TEvent : class
        {
            _applyMethods.AddLambda(handler, typeof(TEvent));
            return this;
        }

        public V4AggregateProjection<T> ProjectEventAsync<TEvent>(Func<T, TEvent, Task> handler)
            where TEvent : class
        {
            _applyMethods.AddLambda(handler, typeof(TEvent));
            return this;
        }

        public V4AggregateProjection<T> ProjectEventAsync<TEvent>(Func<IQuerySession, T, TEvent, Task> handler)
        {
            _applyMethods.AddLambda(handler, typeof(TEvent));
            return this;
        }
    }
}
