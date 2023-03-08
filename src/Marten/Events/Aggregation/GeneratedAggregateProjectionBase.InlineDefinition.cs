using System;
using System.Threading.Tasks;

namespace Marten.Events.Aggregation;

public abstract partial class GeneratedAggregateProjectionBase<T>: IAggregationSteps<T>
{
    public IAggregationSteps<T> CreateEvent<TEvent>(Func<TEvent, T> creator) where TEvent : class
    {
        _createMethods.AddLambda(creator, typeof(TEvent));
        return this;
    }

    public IAggregationSteps<T> CreateEvent<TEvent>(Func<TEvent, IQuerySession, Task<T>> creator) where TEvent : class
    {
        _createMethods.AddLambda(creator, typeof(TEvent));
        return this;
    }

    public IAggregationSteps<T> DeleteEvent<TEvent>() where TEvent : class
    {
        DeleteEvents.Add(typeof(TEvent));
        return this;
    }

    public IAggregationSteps<T> DeleteEvent<TEvent>(Func<TEvent, bool> shouldDelete) where TEvent : class
    {
        _shouldDeleteMethods.AddLambda(shouldDelete, typeof(TEvent));
        return this;
    }

    public IAggregationSteps<T> DeleteEvent<TEvent>(Func<T, TEvent, bool> shouldDelete) where TEvent : class
    {
        _shouldDeleteMethods.AddLambda(shouldDelete, typeof(TEvent));
        return this;
    }


    public IAggregationSteps<T> DeleteEventAsync<TEvent>(
        Func<IQuerySession, T, TEvent, Task<bool>> shouldDelete) where TEvent : class
    {
        _shouldDeleteMethods.AddLambda(shouldDelete, typeof(TEvent));
        return this;
    }

    public IAggregationSteps<T> ProjectEvent<TEvent>(Action<T> handler)
        where TEvent : class
    {
        _applyMethods.AddLambda(handler, typeof(TEvent));
        return this;
    }

    public IAggregationSteps<T> ProjectEvent<TEvent>(Action<T, TEvent> handler)
        where TEvent : class
    {
        _applyMethods.AddLambda(handler, typeof(TEvent));
        return this;
    }

    public IAggregationSteps<T> ProjectEvent<TEvent>(Func<T, TEvent, T> handler)
        where TEvent : class
    {
        _applyMethods.AddLambda(handler, typeof(TEvent));
        return this;
    }

    public IAggregationSteps<T> ProjectEvent<TEvent>(Func<T, T> handler)
        where TEvent : class
    {
        _applyMethods.AddLambda(handler, typeof(TEvent));
        return this;
    }

    public IAggregationSteps<T> ProjectEvent<TEvent>(Action<IQuerySession, T, TEvent> handler)
        where TEvent : class
    {
        _applyMethods.AddLambda(handler, typeof(TEvent));
        return this;
    }

    public IAggregationSteps<T> ProjectEventAsync<TEvent>(Func<IQuerySession, T, TEvent, Task> handler)
    {
        _applyMethods.AddLambda(handler, typeof(TEvent));
        return this;
    }

    public IAggregationSteps<T> ProjectEventAsync<TEvent>(Func<IQuerySession, T, TEvent, Task<T>> handler)
    {
        _applyMethods.AddLambda(handler, typeof(TEvent));
        return this;
    }

    public IAggregationSteps<T> TransformsEvent<TEvent>() where TEvent : class
    {
        TransformedEvents.Add(typeof(TEvent));
        return this;
    }
}
