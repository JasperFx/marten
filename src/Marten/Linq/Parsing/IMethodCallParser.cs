using System.Linq.Expressions;
using Marten.Schema;

namespace Marten.Linq.Parsing
{
    // SAMPLE: IMethodCallParser
    /// <summary>
    /// Models the Sql generation for a method call
    /// in a Linq query. For example, map an expression like Where(x => x.Property.StartsWith("prefix"))
    /// to part of a Sql WHERE clause
    /// </summary>
    public interface IMethodCallParser
    {
        /// <summary>
        /// Can this parser create a Sql where clause
        /// from part of a Linq expression that calls
        /// a method
        /// </summary>
        /// <param name="expression"></param>
        /// <returns></returns>
        bool Matches(MethodCallExpression expression);

        /// <summary>
        /// Creates an IWhereFragment object that Marten
        /// uses to help construct the underlying Sql
        /// command
        /// </summary>
        /// <param name="mapping"></param>
        /// <param name="serializer"></param>
        /// <param name="expression"></param>
        /// <returns></returns>
        // TODO -- eliminate serializer as a call here
        IWhereFragment Parse(IQueryableDocument mapping, ISerializer serializer, MethodCallExpression expression);
    }

    // ENDSAMPLE
}
