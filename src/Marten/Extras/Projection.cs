using System.Collections.Generic;

namespace Marten.Extras
{
    public class Projection<TSource, TProjection>
    {
        public readonly IEnumerable<string> PropertiesToInclude;
        public Projection(IEnumerable<string> propertiesToInclude)
        {
            PropertiesToInclude = propertiesToInclude;
        }
    }
}