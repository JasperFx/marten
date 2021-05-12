using System;
using System.Collections.Generic;
using System.Linq;

namespace Marten.Internal.CompiledQueries
{
    internal class EnumParameterFinder : IParameterFinder
    {
        public bool Matches(Type memberType)
        {
            return memberType.IsEnum;
        }

        public bool AreValuesUnique(object query, CompiledQueryPlan plan)
        {
            var groups = plan.Parameters.Where(x => x.Type.IsEnum)
                .GroupBy(x => x.Type).Where(x => x.Count() > 1).ToArray();

            if (!groups.Any()) return true;

            foreach (var grouping in groups)
            {
                var distinctValueCount = grouping.Select(x => x.GetValueAsObject(query)).Distinct().Count();
                if (distinctValueCount != grouping.Count())
                {
                    return false;
                }
            }

            return true;


        }

        public Queue<object> UniqueValueQueue(Type type)
        {
            var enumValues = type.GetEnumValues();
            return new Queue<object>(enumValues.OfType<object>());
        }
    }
}
