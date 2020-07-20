using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Marten.Internal.CompiledQueries
{
    public class UniqueValueSource
    {
        private readonly Dictionary<Type, Queue<object>> _values = new Dictionary<Type, Queue<object>>();

        public object GetValue(Type type)
        {
            if (_values.TryGetValue(type, out var queue))
            {
                return queue.Dequeue();
            }
            else
            {
                queue = QueryCompiler.Finders.Single(x => x.Matches(type))
                    .UniqueValueQueue(type);

                _values.Add(type, queue);

                return queue.Dequeue();
            }
        }

        public object[] ArgsFor(ConstructorInfo constructor)
        {
            return constructor.GetParameters().Select(parameter => GetValue(parameter.ParameterType))
                .ToArray();
        }
    }
}
