using System;
using System.Linq;
using System.Linq.Expressions;
using Baseline;
using Marten.Linq;
using Marten.Linq.Parsing;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Remotion.Linq.Clauses;
using Remotion.Linq.Clauses.Expressions;
using Xunit;

namespace Marten.Testing.Linq.Internals
{
    public class SubQueryFilterParserTests
    {

        private SubQueryExpression forExpression(Expression<Func<Target, bool>> filter)
        {
            Expression<Func<IQueryable<Target>, IQueryable<Target>>> query = q => q.Where(filter);

            var invocation = Expression.Invoke(query, Expression.Parameter(typeof(IQueryable<Target>)));


            var model = MartenQueryParser.Flyweight.GetParsedQuery(invocation);

            return (SubQueryExpression) model.BodyClauses.Single().As<WhereClause>().Predicate;
        }

        [Fact]
        public void try_stuff()
        {
            var expression = forExpression(x => x.Children.Any(c => c.Children.Length > 2));
            expression.ShouldNotBeNull();
        }
    }
}
