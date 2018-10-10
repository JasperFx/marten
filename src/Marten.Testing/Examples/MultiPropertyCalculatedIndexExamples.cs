using System;
using System.Linq.Expressions;

namespace Marten.Testing.Examples
{
    public class MultiPropertyCalculatedIndexExamples
    {
        public void Example()
        {
            // SAMPLE: multi-property-calculated-index
            var store = DocumentStore.For(_ =>
            {
                var columns = new Expression<Func<User, object>>[]
                {
                    x => x.FirstName,
                    x => x.LastName
                };
                _.Schema.For<User>().Index(columns);
            });
            // ENDSAMPLE
        }
    }
}