using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Baseline;
using Marten.Linq.Parsing;
using Marten.Schema;

namespace Marten.Linq.SoftDeletes
{
    public class DeletedSinceParser : IMethodCallParser
    {
        private static readonly MethodInfo _method =
            typeof(SoftDeletedExtensions).GetMethod(nameof(SoftDeletedExtensions.DeletedSince));

        public bool Matches(MethodCallExpression expression)
        {
            return Equals(expression.Method, _method);
        }

        public IWhereFragment Parse(IQueryableDocument mapping, ISerializer serializer, MethodCallExpression expression)
        {
            if (mapping.DeleteStyle != DeleteStyle.SoftDelete)
                throw new NotSupportedException($"Document DeleteStyle must be {DeleteStyle.SoftDelete}");

            var time = expression.Arguments.Last().Value().As<DateTimeOffset>();

            return new WhereFragment($"d.{DocumentMapping.DeletedColumn} and d.{DocumentMapping.DeletedAtColumn} > ?", time);
        }
    }
}
