using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Baseline;

namespace Marten.Events.Projections
{
    public class Aggregator<T> where T : class, new()
    {
        public static readonly string ApplyMethod = "Apply";

        private readonly IDictionary<Type, object> _aggregations = new Dictionary<Type, object>();


        public Aggregator()
        {
            typeof (T).GetMethods()
                .Where(x => x.Name == ApplyMethod && x.GetParameters().Length == 1)
                .Each(method =>
                {
                    var eventType = Enumerable.Single<ParameterInfo>(method.GetParameters()).ParameterType;
                    var step = typeof (AggregationStep<,>)
                        .CloseAndBuildAs<object>(method, typeof (T), eventType);

                    _aggregations.Add(eventType, step);
                });
        }

        public void Add<TEvent>(IAggregation<T, TEvent> aggregation)
        {
            if (_aggregations.ContainsKey(typeof (TEvent)))
            {
                _aggregations[typeof (TEvent)] = aggregation;
            }
            else
            {
                _aggregations.Add(typeof(TEvent), aggregation);
            }
        }

        public void Add<TEvent>(Action<T, TEvent> application)
        {
            Add(new AggregationStep<T, TEvent>(application));
        }

        public IAggregation<T, TEvent> AggregatorFor<TEvent>()
        {
            return _aggregations.ContainsKey(typeof (TEvent))
                ? _aggregations[typeof (TEvent)].As<IAggregation<T, TEvent>>()
                : null;
        }


        public bool AppliesTo(EventStream stream)
        {
            return stream.Events.Any(x => _aggregations.ContainsKey(x.Data.GetType()));
        }
    }
}