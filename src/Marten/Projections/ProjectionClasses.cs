using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Events;

namespace Marten.Projections
{

    // May be just a marker
    public interface ITransform
    {
        Type FromType { get; }
        Type ToType { get; }
    }

    // For mapping a single event to a different representation
    public interface ITransform<TFrom, TTo> : ITransform
    {
        TTo Transform(TFrom from);
    }

    public interface IUpdater<TAggregate>
    {
        Type EventType { get; }
    }

    // TODO -- support partial updates later
    public interface IUpdater<TAggregate, TEvent> : IUpdater<TAggregate> where TEvent : IEvent
    {
        TAggregate Update(TAggregate existing, TEvent @event);
    }


    // This is for nothing but being able to apply
    // state changes to some kind of aggregated view
    // across multiple events
    public interface IAggregator<T>
    {
        T Apply(IEvent @event);
    }

    // This would be used to create aggregated views across a single event stream
    public class ByStreamProjection<TAggregate> : IProjection where TAggregate : IAggregate, new()
    {

        public ByStreamProjection(IEnumerable<IUpdater<TAggregate>> updaters)
        {
        }

        public IEnumerable<Type> AppliesTo()
        {
            // All of the updaters
            throw new NotImplementedException();
        }

        public IEnumerable<Type> Publishes()
        {
            yield return typeof (TAggregate);
        }

        public void Apply(IDocumentSession session)
        {
            

            // look at the EventStream's. If any matches the aggregate type, or the event types,
            // swing into action.

            throw new NotImplementedException();
        }

        public Task ApplyAsync(IDocumentSession session, CancellationToken token)
        {
            // look at the EventStream's. If any matches the aggregate type, or the event types,
            // swing into action.

            throw new NotImplementedException();
        }
    }




    public interface IProjection
    {
        // We'll need these two methods to be able to appropriately
        // sort the application of projections, so that one projection
        // can feed another
        IEnumerable<Type> AppliesTo();
        IEnumerable<Type> Publishes();

        // Everything you need is exposed off of the IDocumentSession
        // So use PendingChanges() to see the staged events, add more documents
        // to write changes
        void Apply(IDocumentSession session);

        Task ApplyAsync(IDocumentSession session, CancellationToken token);
    }

    public class ProjectionCalculator : DocumentSessionListenerBase
    {
        private readonly ProjectionGraph _projections;

        public ProjectionCalculator(ProjectionGraph projections)
        {
            _projections = projections;
        }

        public override void BeforeSaveChanges(IDocumentSession session)
        {
            var pending = session.PendingChanges.AllChangedFor<EventStream>().ToArray();

            if (pending.Any())
            {
                _projections.ApplyInline(session, pending);
            }
        }

        public override async Task BeforeSaveChangesAsync(IDocumentSession session)
        {
            throw new NotImplementedException();

            var pending = session.PendingChanges.AllChangedFor<EventStream>().ToArray();

            if (pending.Any())
            {
                // TODO -- need to push a CancellationToken through the listener interface here.
                //await _projections.ApplyInlineAsync(session, pending);
            }
        }
    }


    public class ProjectionGraph
    {
        private readonly IList<IProjection> _inlineProjections = new List<IProjection>();

        // Store IAggregator<T>'s here for live aggregations
        private readonly IDictionary<Type, object> _liveAggregations = new Dictionary<Type, object>();

        private readonly IList<IProjection> _asyncProjections = new List<IProjection>();


        public void ApplyInline(IDocumentSession session, IEnumerable<EventStream> streams)
        {
            _inlineProjections.Each(x => x.Apply(session));
        }

        public async Task ApplyInlineAsync(IDocumentSession session, IEnumerable<EventStream> streams, CancellationToken token)
        {
            // TODO -- might weed out by applicability to avoid so many Task's
            foreach (var projection in _inlineProjections)
            {
                await projection.ApplyAsync(session, token).ConfigureAwait(false);
            }
        }

        public async Task ApplyAsync(IDocumentSession session, CancellationToken token)
        {
            // TODO -- might weed out by applicability to avoid so many Task's
            foreach (var projection in _asyncProjections)
            {
                await projection.ApplyAsync(session, token);
            }
        }

    }
    
}