using System.Linq;
using System.Linq.Expressions;
using Baseline;
using Marten.Schema;

namespace Marten.Linq.Handlers
{
    public class StringEndsWith : MethodCallParser<string>
    {
        public StringEndsWith() : base(x => x.EndsWith(null))
        {
        }

        public override IWhereFragment Parse(IDocumentMapping mapping, ISerializer serializer, MethodCallExpression expression)
        {
            var @object = expression.Object;
            var locator = mapping.JsonLocator(@object);
            var value = expression.Arguments.Single().Value().As<string>();
            return new WhereFragment("{0} like ?".ToFormat(locator), "%" + value);
        }
    }
}