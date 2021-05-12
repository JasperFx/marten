using System.Linq.Expressions;
using System.Reflection;
using Marten.Linq.Fields;
using Marten.Linq.Filters;
using Marten.Linq.Parsing;
using Marten.Linq.SqlGeneration;
using Marten.Schema;

namespace Marten.Linq.SoftDeletes
{
    internal class MaybeDeletedParser: IMethodCallParser
    {
        private static readonly MethodInfo _method =
            typeof(SoftDeletedExtensions).GetMethod(nameof(SoftDeletedExtensions.MaybeDeleted));

        private static readonly WhereFragment _whereFragment = new WhereFragment($"d.{SchemaConstants.DeletedColumn} is not null");

        public bool Matches(MethodCallExpression expression)
        {
            return expression.Method == _method;
        }

        public ISqlFragment Parse(IFieldMapping mapping, ISerializer serializer, MethodCallExpression expression)
        {
            return _whereFragment;
        }
    }
}
