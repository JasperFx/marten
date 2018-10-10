using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Marten.Util;

namespace Marten.Events.Projections
{
    public class EventAggregationStep<T, TEvent> : IAggregationWithMetadata<T, TEvent>
    {
        private readonly Action<T, Event<TEvent>> _apply;

        public EventAggregationStep(MethodInfo method)
        {
            if (method.GetParameters().Length != 1 
                || method.GetParameters().Single().ParameterType != typeof (Event<TEvent>) 
                || method.DeclaringType != typeof (T))
            {
                throw new ArgumentOutOfRangeException($"Method {method.Name} on {method.DeclaringType} cannot be used as an aggregation method");
            }

            var aggregateParameter = Expression.Parameter(typeof (T), "a");
            var eventParameter = Expression.Parameter(typeof(Event<TEvent>), "e");

            var body = Expression.Call(aggregateParameter, method, eventParameter);

            var lambda = Expression.Lambda<Action<T, Event<TEvent>>>(body, aggregateParameter, eventParameter);

            _apply = ExpressionCompiler.Compile<Action<T, Event<TEvent>>>(lambda);
        } 

        public EventAggregationStep(Action<T, Event<TEvent>> apply)
        {
            _apply = apply;
        }

        public void Apply(T aggregate, TEvent @event)
        {
            throw new NotSupportedException("Should never be called");
        }

        public void Apply(T aggregate, Event<TEvent> @event)
        {
            _apply(aggregate, @event);
        }
    }
}