using System;
using System.Linq.Expressions;
using Marten.Testing.Documents;

namespace Marten.Testing.Examples
{
    public class MultiPropertyCalculatedIndexExamples
    {
        public void Example()
        {
            #region sample_multi-property-calculated-index
            var store = DocumentStore.For(_ =>
            {
                var columns = new Expression<Func<User, object>>[]
                {
                    x => x.FirstName,
                    x => x.LastName
                };
                _.Schema.For<User>().Index(columns);
            });
            #endregion sample_multi-property-calculated-index
        }
    }
}
