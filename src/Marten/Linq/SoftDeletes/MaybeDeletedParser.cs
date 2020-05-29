using System.Linq.Expressions;
using System.Reflection;
using Marten.Linq.Fields;
using Marten.Linq.Parsing;
using Marten.Schema;

namespace Marten.Linq.SoftDeletes
{
    public class MaybeDeletedParser: IMethodCallParser
    {
        private static readonly MethodInfo _method =
            typeof(SoftDeletedExtensions).GetMethod(nameof(SoftDeletedExtensions.MaybeDeleted));

        private static readonly WhereFragment _whereFragment = new WhereFragment($"d.{DocumentMapping.DeletedColumn} is not null");

        public bool Matches(MethodCallExpression expression)
        {
            return expression.Method == _method;
        }

        public IWhereFragment Parse(IFieldMapping mapping, ISerializer serializer, MethodCallExpression expression)
        {
            return _whereFragment;
        }
    }
}
