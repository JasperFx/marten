using System.Linq.Expressions;
using Marten.Linq.Fields;
using Marten.Linq.SqlGeneration;
using Marten.Schema;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Parsing
{
    public interface IExpressionParser<in T> where T : Expression
    {
        /// <summary>
        /// Can this parser create a Sql where clause
        /// from part of a Linq expression
        /// </summary>
        bool Matches(T expression);

        /// <summary>
        /// Creates an ISqlFragment object that Marten
        /// uses to help construct the underlying Sql
        /// command
        /// </summary>
        ISqlFragment Parse(IFieldMapping mapping, ISerializer serializer, T expression);
    }
}
