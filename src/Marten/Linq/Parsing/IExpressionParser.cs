using System.Linq.Expressions;
using Marten.Schema;

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
        /// Creates an IWhereFragment object that Marten
        /// uses to help construct the underlying Sql
        /// command
        /// </summary>
        IWhereFragment Parse(IQueryableDocument mapping, ISerializer serializer, T expression);
    }
}