using System.Linq.Expressions;
using System.Reflection;
using Marten.Linq.Parsing;
using Marten.Schema;

namespace Marten.Linq.SoftDeletes
{
    public class IsDeletedParser: IMethodCallParser
    {
        private static readonly MethodInfo _method =
            typeof(SoftDeletedExtensions).GetMethod(nameof(SoftDeletedExtensions.IsDeleted));

        private static readonly WhereFragment _whereFragment = new WhereFragment($"d.{DocumentMapping.DeletedColumn} = True");

        public bool Matches(MethodCallExpression expression)
        {
            return expression.Method == _method;
        }

        public IWhereFragment Parse(IQueryableDocument mapping, ISerializer serializer, MethodCallExpression expression)
        {
            return _whereFragment;
        }
    }
}
