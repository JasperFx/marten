using System.Linq.Expressions;
using Marten.Schema;

namespace Marten.Linq.Handlers
{
    public interface IMethodCallParser
    {
        bool Matches(MethodCallExpression expression);
        IWhereFragment Parse(
            IDocumentMapping mapping, 
            ISerializer serializer, 
            MethodCallExpression expression
            );
    }
}