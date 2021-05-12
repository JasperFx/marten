using System;
using System.Collections.Generic;
using System.Linq;

namespace Marten.Internal.CompiledQueries
{
    internal class SimpleParameterFinder<T> : IParameterFinder
    {
        private readonly Func<int, T[]> _uniqueValues;

        public SimpleParameterFinder(Func<int, T[]> uniqueValues)
        {
            _uniqueValues = uniqueValues;
        }

        public Type DotNetType => typeof(T);

        public Queue<object> UniqueValueQueue(Type type)
        {
            return new Queue<object>(_uniqueValues(100).OfType<object>());
        }

        public bool Matches(Type memberType)
        {
            return memberType == DotNetType;
        }

        public bool AreValuesUnique(object query, CompiledQueryPlan plan)
        {
            var members = findMembers(plan);

            if (!members.Any()) return true;

            return members.Select(x => x.GetValue(query))
                .Distinct().Count() == members.Length;
        }

        private static IQueryMember<T>[] findMembers(CompiledQueryPlan plan)
        {
            return plan.Parameters.OfType<IQueryMember<T>>().ToArray();
        }

    }
}
