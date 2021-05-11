using System.Linq.Expressions;
using System.Reflection;
using Marten.Linq.Fields;
using Marten.Linq.Parsing;
using Marten.Linq.SqlGeneration;

namespace Marten.Events.Archiving
{
    internal class MaybeArchivedMethodCallParser: IMethodCallParser
    {
        private static readonly MethodInfo _method =
            typeof(ArchivedEventExtensions).GetMethod(nameof(ArchivedEventExtensions.MaybeArchived));

        private static readonly ISqlFragment _whereFragment = new AllEventsFilter();


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