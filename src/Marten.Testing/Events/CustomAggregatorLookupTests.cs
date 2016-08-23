using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Baseline;
using Marten.Events;
using Marten.Events.Projections;
using Marten.Util;
using Shouldly;
using Xunit;

namespace Marten.Testing.Events
{
    public class CustomAggregatorLookupTests
    {
        private readonly EventGraph theGraph = new EventGraph(new StoreOptions());

        public CustomAggregatorLookupTests()
        {
            theGraph.UseAggregatorLookup(new AggregatorLookup(type => typeof(AggregatorUsePrivateApply<>).CloseAndBuildAs<IAggregator>(type)));
        }

        [Fact]
        public void can_lookup_private_apply_methods()
        {
            var aggregator = theGraph.AggregateFor<AggregateWithPrivateEventApply>();

            var stream = new EventStream(Guid.NewGuid(), false)
                .Add(new QuestStarted {Name = "Destroy the Ring"});                

            var party = aggregator.Build(stream.Events, null);

            party.Name.ShouldBe("Destroy the Ring");
        }      
    }

    public class AggregatorUsePrivateApply<T> : IAggregator<T> where T : class, new()
    {
        public static readonly string ApplyMethod = "Apply";

        private readonly IDictionary<Type, object> _aggregations = new Dictionary<Type, object>();


        public AggregatorUsePrivateApply()
        {
            typeof(T).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
                .Where(x => x.Name == ApplyMethod && x.GetParameters().Length == 1)
                .Each(method =>
                {
                    var eventType = method.GetParameters().Single<ParameterInfo>().ParameterType;
                    var step = typeof(AggregationStep<,>)
                        .CloseAndBuildAs<object>(method, typeof(T), eventType);

                    _aggregations.Add(eventType, step);
                });

            Alias = typeof(T).Name.ToTableAlias();
        }

        public Type AggregateType => typeof(T);

        public string Alias { get; }

        public T Build(IEnumerable<IEvent> events, IDocumentSession session)
        {
            var state = new T();

            events.Each(x => x.Apply(state, this));

            return state;
        }

        public Type[] EventTypes => _aggregations.Keys.ToArray();

        public AggregatorUsePrivateApply<T> Add<TEvent>(IAggregation<T, TEvent> aggregation)
        {
            if (_aggregations.ContainsKey(typeof(TEvent)))
            {
                _aggregations[typeof(TEvent)] = aggregation;
            }
            else
            {
                _aggregations.Add(typeof(TEvent), aggregation);
            }

            return this;
        }

        public AggregatorUsePrivateApply<T> Add<TEvent>(Action<T, TEvent> application)
        {
            return Add(new AggregationStep<T, TEvent>(application));
        }

        public IAggregation<T, TEvent> AggregatorFor<TEvent>()
        {
            return _aggregations.ContainsKey(typeof(TEvent))
                ? _aggregations[typeof(TEvent)].As<IAggregation<T, TEvent>>()
                : null;
        }


        public bool AppliesTo(EventStream stream)
        {
            return stream.Events.Any(x => _aggregations.ContainsKey(x.Data.GetType()));
        }
    }

    public class AggregateWithPrivateEventApply
    {
        public Guid Id { get; set; }
        
        private void Apply(QuestStarted started)
        {
            Name = started.Name;
        }

        public string Name { get; private set; }
    }
}